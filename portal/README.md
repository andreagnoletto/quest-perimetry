# portal — Fases 3 e 4 (Next.js + PostgreSQL)

> Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA

Portal do médico: cadastro de pacientes, pedido de exame para o headset, importação dos arquivos do headset,
relatório **Single Field Analysis** no layout HFA (tela e PDF A4), série de escala de cinza e tendência de MD/VFI,
calibração fotométrica por headset, exportação pseudonimizada para pesquisa e log de acesso. Na Fase 4, o headset
pareado recebe os pedidos pela rede local, envia os exames sozinho e transmite o exame **ao vivo** (`/live`), com
pausar/retomar/encerrar pelo portal (contrato em `docs/SYNC_API.md`).

O portal **não calcula nada clínico**: limiares, TD/PD, probabilidades, MD/PSD/VFI/GHT e avisos vêm do CLI do `/core`
(`perimetry report`), o mesmo código que roda no headset. O portal só guarda, mostra e imprime.

## Rodar localmente

Precisa de Node 22.18+, .NET 8 SDK e PostgreSQL 14+.

```bash
dotnet build core/Perimetry.Cli -c Release          # na raiz do repositório
cd portal
cp .env.example .env                                  # preencha DATABASE_URL e PERIMETRY_DATA_KEY
npm ci
npx prisma migrate deploy                            # cria as tabelas
npm run create-user -- --email voce@clinica --name "Dr(a). Nome" --role admin
npm run build && npm start                           # http://localhost:3000
```

`PERIMETRY_DATA_KEY` (32 bytes em base64) criptografa nome, ID interno, nascimento, sexo, refração, diagnóstico e as
chaves dos headsets. **Sem ela os dados ficam ilegíveis**: guarde uma cópia fora do servidor. O PDF usa o Chromium
do Playwright (`npx playwright install chromium`) ou o indicado em `PERIMETRY_CHROMIUM_PATH`. Atrás de HTTPS, use
`PERIMETRY_SECURE_COOKIES=true`.

## Fluxo

1. **Paciente** → cadastrar com consentimento (obrigatório) e, se ele quiser, autorização de pesquisa. O portal gera
   um código pseudônimo (`P-XXXXXX`), o único dado do paciente que vai para o headset.
2. **Pedido de exame** → escolher o exame (presets de `config/headset.json`) e **enviar ao headset pareado** (aparece
   na tela do operador na próxima abertura do app) ou baixar `exam-order.json` e copiar para a pasta do app
   (ver `headset/README.md`).
3. **Headsets e calibração** → *Parear headset* gera um código de uso único (15 min) e o `portal.json` para copiar
   ao headset (`adb push`); na próxima abertura o app troca o código por um token e envia a chave `device.key`.
   Sem rede, cadastre o headset à mão com o conteúdo de `device.key`. Enviar a calibração fotométrica; o portal
   valida com o `/core` e oferece o `calibration.json`. Tokens podem ser revogados na mesma página.
4. **Ao vivo** → com o headset pareado, cada sessão aparece em *Ao vivo*: estado, mapa de limiares preenchendo
   (dB equiv. HFA), último estímulo, perdas de fixação/FP/FN e os botões *Pausar / Retomar / Encerrar*, que o
   headset aplica em até 1 s (uma pausa remota é idêntica à pausa pelo botão do paciente).
5. **Importar** → automático pelo headset pareado (arquivos chegam assinados e já verificados) ou manual: enviar `exam_*.json`, `session_*.json` e os `.hmac`. Cada exame é analisado, vinculado ao paciente
   pelo código e marcado como integridade *verificada*, *não verificada* (sem chave/assinatura) ou *FALHOU*.
6. **Relatório** → tela, PDF A4 (uma página por olho), CSV dos pontos, JSON da análise e arquivo original.

## Perfis

| Perfil | Pode |
|---|---|
| `operator` | pacientes, pedidos, importação, relatórios |
| `doctor` | o mesmo + headsets, chaves e calibração |
| `admin` | tudo + exportação para pesquisa e log de acesso |

## LGPD

- Campos identificadores criptografados (AES-256-GCM) com índices cegos (HMAC) para busca exata por nome/ID.
- Senhas com scrypt; sessão em cookie `httpOnly`, banco guarda só o hash do token.
- Todo acesso a dado de paciente entra no log (`/audit`): ver, criar, editar, imprimir, exportar, importar.
- Exportação de pesquisa só de pacientes que autorizaram, com ID de estudo, idade em anos e dias desde o primeiro
  exame; sem nome, ID, nascimento, código, diagnóstico ou datas.
- O arquivo bruto do headset é guardado byte a byte para reanálise e reverificação da assinatura.
- Headset: token por aparelho (só o hash fica no banco), revogável; só o código pseudônimo trafega para ele. Em rede
  local sem HTTPS o token trafega em claro — use um proxy reverso com TLS quando possível (`PERIMETRY_SECURE_COOKIES`).
- Chamadas do headset entram no log de acesso como `headset.*`, com o serial do aparelho.
- Fase 6: senhas de 12+ caracteres, bloqueio da conta após 10 erros (15 min), limite por IP, sessão expira sem uso,
  troca de senha em "Minha conta"; CSP com nonce e demais cabeçalhos de segurança; exportação dos dados de um paciente
  (direito de acesso) e **anonimização** em vez de exclusão (exames ficam sob o código); versão do texto de
  consentimento gravada; log de acesso em CSV; rotação de chave por `scripts/rotate-key.ts`. Guia: [SECURITY.md](SECURITY.md).

## Testes

```bash
npm run typecheck
npm test                      # unitários (vitest), inclui integração com o CLI do /core
npm run build && npm run e2e  # Playwright: fluxo do médico (flow.spec), fase 5 (phase5.spec), segurança/LGPD (security.spec), sincronização/ao vivo (sync.spec)
```

O e2e cria um banco novo a cada execução (`E2E_DATABASE_URL` + sufixo de data) com `prisma migrate deploy`; nada
existente é apagado. Bancos `perimetry_e2e_*` antigos podem ser removidos à mão.

## Fora desta fase

Análise de progressão por eventos (GPA), redefinição de senha pela interface (hoje via `npm run create-user`),
WebSocket (o ao vivo usa POST a 1 Hz + Server-Sent Events; ver `docs/SYNC_API.md`), vários processos do portal
(o estado ao vivo fica em memória de um processo).
