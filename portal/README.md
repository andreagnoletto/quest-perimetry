# portal — Fase 3 (Next.js + PostgreSQL)

> Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA

Portal do médico: cadastro de pacientes, pedido de exame para o headset, importação dos arquivos do headset,
relatório **Single Field Analysis** no layout HFA (tela e PDF A4), série de escala de cinza e tendência de MD/VFI,
calibração fotométrica por headset, exportação pseudonimizada para pesquisa e log de acesso.

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
2. **Pedido de exame** → escolher o exame (presets de `config/headset.json`) e baixar `exam-order.json`; copiar
   para a pasta do app no headset (ver `headset/README.md`).
3. **Headset e calibração** → cadastrar o headset com o conteúdo de `device.key` (para verificar a assinatura dos
   exames) e enviar a calibração fotométrica; o portal valida com o `/core` e oferece o `calibration.json`.
4. **Importar** → enviar `exam_*.json`, `session_*.json` e os `.hmac`. Cada exame é analisado, vinculado ao paciente
   pelo código e marcado como integridade *verificada*, *não verificada* (sem chave/assinatura) ou *FALHOU*.
5. **Relatório** → tela, PDF A4 (uma página por olho), CSV dos pontos, JSON da análise e arquivo original.

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

## Testes

```bash
npm run typecheck
npm test                      # unitários (vitest), inclui integração com o CLI do /core
npm run build && npm run e2e  # Playwright: login → paciente → pedido → importação → relatório → PDF → auditoria
```

O e2e cria um banco novo a cada execução (`E2E_DATABASE_URL` + sufixo de data) com `prisma migrate deploy`; nada
existente é apagado. Bancos `perimetry_e2e_*` antigos podem ser removidos à mão.

## Fora desta fase

Tokens de headset e envio direto pela rede (Fase 4), análise de progressão por eventos (GPA), redefinição de senha
pela interface (hoje via `npm run create-user`).
