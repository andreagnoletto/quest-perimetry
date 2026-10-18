# Segurança e LGPD do portal — guia de operação

Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA. Este guia descreve o que o portal
faz para proteger dados de pacientes e o que a clínica precisa fazer por fora (chaves, backup, rede). Decisões
de produto da Fase 6 estão em `docs/CLINICAL_SPEC.md` ("Segurança e LGPD").

## Modelo de ameaça (resumo)

| Ameaça | Mitigação |
|---|---|
| Vazamento do banco (dump, backup perdido) | Identificadores em AES-256-GCM com chave fora do banco (`PERIMETRY_DATA_KEY`); busca só por índice cego (HMAC); sessões e tokens de headset guardados como SHA-256; senhas scrypt. Sem a chave, o dump tem só códigos pseudônimos e exames. |
| Adivinhação de senha | Mínimo 12 caracteres, rejeita senhas triviais; bloqueio da conta por 15 min após 10 erros seguidos (fica no banco, sobrevive a reinício); teto de 50 tentativas por IP a cada 15 min; tudo no log (`login.failed`, `login.locked`, `login.ratelimited`). |
| Sessão roubada ou esquecida aberta | Cookie `httpOnly`, `SameSite=Lax`, `Secure` com `PERIMETRY_SECURE_COOKIES=true`; expira em 12 h ou após 60 min sem uso (`SESSION_IDLE_MINUTES`); sessão nova a cada login; trocar a senha encerra todas as outras sessões. |
| XSS / injeção de script | Content-Security-Policy com nonce por requisição e `strict-dynamic` (`src/middleware.ts`), `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, HSTS atrás de HTTPS. |
| Headset falso ou código de pareamento adivinhado | Código de uso único, 8 caracteres sem ambíguos, com validade; teto de 20 tentativas por IP a cada 15 min (`headset.pair.ratelimited`); token por aparelho revogável e com validade opcional (`DeviceToken.expiresAt`); exames assinados com HMAC pela chave do aparelho. |
| Uso indevido por quem tem acesso | Todo acesso a dado de paciente no log de acesso (quem, quando, o quê, IP), exportável em CSV pelo administrador; perfis `operator`/`doctor`/`admin`. |
| Dependências vulneráveis | CI falha se uma dependência de produção tiver aviso **crítico** (`npm audit --omit=dev --audit-level=critical`); o relatório completo roda informativo. Veja "Avisos conhecidos". |

Fora do escopo do portal: segurança do PC/servidor da clínica, do Wi-Fi e do próprio headset (o headset guarda só
códigos pseudônimos e exames assinados; nunca nome).

## O que fica e o que não fica no log de acesso

Fica: usuário, ação, tipo e id do registro, detalhe curto, IP, data/hora. Também login falho (e-mail tentado),
bloqueio, limite por IP, troca de senha, exportações, anonimização (com o motivo digitado), pareamentos e cada
chamada do headset (`headset.*`). **Não fica**: senha, nome do paciente, texto da busca, conteúdo dos exames, token.

## Direitos do titular (LGPD, art. 18)

- **Acesso / cópia (II)**: na página do paciente, "Exportar dados do paciente (LGPD)" gera um JSON com cadastro,
  consentimentos (texto versionado, `consentVersion`), pedidos e todos os exames (laudo e arquivo bruto). Fica no log
  como `patient.export`.
- **Eliminação (VI)**: "Anonimizar registro" (admin ou médico, confirmação digitando o código). Apaga nome, ID
  interno, nascimento, sexo, refração, diagnóstico e observação do consentimento; retira a autorização de pesquisa;
  cancela pedidos abertos. Exames e laudos continuam sob o código, sem ligação com a pessoa, para auditoria e
  reanálise. **Irreversível.** Fica no log como `patient.anonymize`.
- **Revogação do consentimento de pesquisa**: desmarcar na edição do paciente; a data e o usuário ficam gravados
  (`researchConsentAt/ById`) e o paciente sai da próxima exportação de pesquisa.
- **Versão do consentimento**: o texto apresentado está em `src/lib/consent-text.ts`; mude a versão sempre que o
  texto mudar. Cada paciente guarda a versão com que consentiu.

## Rotação da chave de criptografia

`PERIMETRY_DATA_KEY` criptografa os identificadores dos pacientes e as chaves HMAC dos headsets. Para trocar:

1. Faça backup (abaixo) e pare o portal (ninguém pode gravar durante a troca).
2. Gere a chave nova: `node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"`.
3. Simule: `PERIMETRY_DATA_KEY=<atual> PERIMETRY_DATA_KEY_NEW=<nova> node scripts/rotate-key.ts`
   (mostra quantos registros vão mudar; nada é gravado).
4. Aplique: o mesmo comando com `--apply`. Tudo roda em uma única transação; se falhar, o banco fica na chave
   antiga. O script relê um registro com a chave nova no fim e grava `key.rotate` no log.
5. Troque `PERIMETRY_DATA_KEY` no ambiente do portal pela chave nova e reinicie.
6. Guarde a chave antiga só até confirmar que backups anteriores à troca não serão mais restaurados (um backup
   antigo precisa da chave da época).

Os IDs de estudo da exportação de pesquisa derivam da chave e mudam com a rotação: uma exportação é um recorte
daquela data, não uma série ligável entre chaves.

## Backup e restauração

- Banco: `pg_dump --format=custom --file perimetry_$(date +%F).dump "$DATABASE_URL"`; restauração em um banco
  **novo e vazio** com `pg_restore --dbname <novo> perimetry_<data>.dump`. Nunca restaure por cima do banco em uso;
  nunca use `prisma migrate reset`.
- Chave: `PERIMETRY_DATA_KEY` **não está no dump**. Guarde-a em um cofre separado do backup (gerenciador de senhas
  da clínica, envelope lacrado). Sem a chave, um backup só tem códigos pseudônimos e exames.
- Teste a restauração periodicamente em um banco de teste e abra um paciente no portal apontado para ele.
- O portal roda em um processo (`next start`): o estado ao vivo e os contadores de limite por IP são em memória e
  zeram no reinício; nada disso precisa de backup.

## Implantação recomendada

- Atrás de um proxy reverso com TLS (mesmo na rede local) e `PERIMETRY_SECURE_COOKIES=true`, que também liga HSTS e
  `upgrade-insecure-requests`. Sem TLS, cookies e tokens de headset trafegam em claro na rede da clínica.
- Banco PostgreSQL acessível só pelo servidor do portal; usuário do banco sem privilégio de criar/derrubar bancos
  em produção.
- Um usuário por pessoa (nunca conta compartilhada), perfil mínimo necessário; `admin` só para quem precisa do
  log e da exportação de pesquisa.

## Avisos conhecidos de dependências (18/10/2026)

`npm audit` aponta avisos **altos** (não críticos) em `next`/`postcss` e `prisma`/`deepmerge-ts`, cuja correção
exige Next 16 e Prisma 7 (mudanças incompatíveis, planejadas para uma fase de atualização própria), e um aviso
crítico em `vitest`/`tinypool`, dependência só de desenvolvimento (não vai para produção). O CI bloqueia apenas
avisos críticos em dependências de produção; o relatório completo fica visível em cada execução.
