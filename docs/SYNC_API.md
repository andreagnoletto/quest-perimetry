# Sincronização headset ↔ portal (Fase 4)

> Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA

Contrato entre o headset (`headset/Assets/Perimetry/Logic/PortalSync*.cs`) e o portal (`portal/src/app/api/headset/*`).
O exame **nunca depende da rede**: tudo é gravado localmente primeiro (Fase 2) e enviado depois; sem rede o fluxo
é idêntico ao da Fase 2 (`exam-order.json`, `adb pull`). Versão do contrato: **`perimetry-sync/1`**.

## Arquivos no headset (`/sdcard/Android/data/br.questperimetry.headset/files/`)

| Arquivo | Quem escreve | Conteúdo |
|---|---|---|
| `portal.json` | operador (baixado da página *Headsets* do portal, copiado por `adb push`) | `{"url":"http://192.168.0.10:3000","pairingCode":"7K3M-9Q2X"}` |
| `portal-token.json` | headset, após parear | `{"url":...,"deviceId":...,"token":...,"pairedUtc":...}`. Apagar = desparear. |
| `portal.used-<ts>.json` | headset | `portal.json` renomeado após o pareamento |
| `exams/<arquivo>.sent` | headset | recibo do portal para `<arquivo>` (`{"examDbId","status","sentUtc"}`); sem `.sent` = ainda na fila de envio |

Se `portal.json` existir com um `pairingCode` mesmo havendo token, o headset pareia de novo (troca de portal).

## Autenticação

Toda chamada, exceto `pair`, leva `Authorization: Bearer <token>`. O token é aleatório (32 bytes, base64url); o
portal guarda só o SHA-256. Token inválido ou revogado → `401 {"error":"unauthorized"}`. Toda chamada entra no log
de acesso (`AccessLog`, `userId` nulo, `action` `headset.*`, `detail` = serial do headset).

Todas as respostas são JSON com `"schema":"perimetry-sync/1"`. Erros: `{"schema","error":"<codigo>","message"}`.

## Endpoints

### `POST /api/headset/pair` (sem token)
Corpo: `{"code":"7K3M-9Q2X","serial":"<DeviceSerial>","model":"Quest 3","hmacKey":"<device.key base64>","software":"0.2.0-phase2/0.3.0"}`
- Código: 8 caracteres do alfabeto `23456789ABCDEFGHJKMNPQRSTUVWXYZ`, exibido como `XXXX-XXXX`; comparação ignora hífens e caixa.
  Vale 15 minutos e é invalidado após 5 tentativas erradas. Gerado pelo médico/admin na página *Headsets*.
- Efeito: cria ou atualiza o `Device` pelo serial, grava a chave HMAC (criptografada) — por isso exames enviados pela rede
  já saem com integridade **verificada** — e emite um `DeviceToken`. Tokens anteriores do mesmo headset são revogados.
- `200 {"schema","deviceId","token","portalName","serverTimeUtc"}` · `403 {"error":"invalid_code"}` · `400 {"error":"bad_request"}`

### `GET /api/headset/orders`
Pedidos de exame **enviados a este headset** (`ExamOrder.deviceId`), ainda não realizados nem cancelados, do mais novo
para o mais antigo, no máximo 20. Mesmos campos do `exam-order.json` mais `id`:
`{"schema","orders":[{"id","patientRef","ageYears","refractiveCorrection","presetId","seed":null|number,"createdUtc"}],"serverTimeUtc"}`

O headset mostra o pedido ao operador na tela de preparação (código pseudônimo, exame, idade); com vários pedidos o
direcional alterna entre eles e a última opção é "sem pedido" (fluxo manual). O pedido escolhido vai em `orderId` no
painel ao vivo e no log de sessão (`SessionLog.Order`).

### `POST /api/headset/exams`
Envio dos arquivos de uma sessão (ou de sessões antigas ainda sem `.sent`), texto bruto + HMAC, exatamente como estão
no disco:
`{"schema","sessionId":"<SessionId>","files":[{"name":"exam_....json","text":"<conteúdo>","hmac":"<hex do .hmac>"}]}`
→ `{"schema","results":[{"name","status":"imported|duplicate|error","message","examDbId":null|string,"integrity":"verified|unverified|failed"}]}`

O portal importa com o mesmo `importFiles` da importação manual (análise pelo `/core`, vínculo pelo `patientRef`,
pedido marcado como realizado). `imported` e `duplicate` fecham o arquivo (headset grava `.sent`); `error` fica na fila
e é tentado de novo na próxima sessão/abertura do app. Corpo até 20 MB; o headset envia no máximo 10 arquivos por chamada.

### `POST /api/headset/live`
Instantâneo da sessão em andamento, enviado a cada mudança de estado e no mínimo a cada `sync.liveIntervalMs`
(padrão 1000 ms) enquanto a sessão não termina; o último envio leva `"finished":true`.
```json
{"schema":"perimetry-sync/1","sessionId":"ab12...","seq":42,"sentUtc":"2026-10-18T17:00:00Z",
 "state":"Exam","paused":false,"finished":false,"outcome":null,
 "patientRef":"P-7K3M9Q","orderId":"clx...|null","presetId":"24-2-zest","ageYears":63,"eye":"OD",
 "startedUtc":"2026-10-18T16:55:00Z","elapsedMs":123456,
 "exam":{"examId":"...","gridId":"24-2","strategy":"Zest","eye":"OD","hfaOffsetDb":15.5,"minMeasurableHfaDb":15.5,
   "locations":[{"i":0,"x":-9,"y":21,"blindSpot":false,"state":"NotStarted|Active|Finished","db":null|27.3,"hfaEqDb":null|42.8,"censoredLow":false,"presentations":3}],
   "reliability":{"fixationLosses":{"errors":1,"trials":5,"alertLimit":0.2},"falsePositives":{"errors":0,"trials":3,"alertLimit":0.15},"falseNegatives":{"errors":0,"trials":2,"alertLimit":0.33},
                  "totalPresentations":120,"blindSpotFound":true,"blindSpotRemaps":0,"durationMs":100000},
   "last":{"x":9,"y":-3,"db":24,"seen":true,"kind":"Threshold","tMs":99000}},
 "ackCommandIds":["cmd1"]}
```
- `exam` é nulo fora do exame (boas-vindas, treino, entre olhos). `locations` seguem a convenção do `ExamRecord`
  (OD; o portal espelha OS como no laudo). `db` é dB do aparelho; `hfaEqDb` = dB equivalente HFA calculado no
  headset pelo `/core` (o portal não calcula nada clínico). `last` está em coordenadas do olho testado (visão do paciente).
- Resposta: `{"schema","commands":[{"id","type":"pause|resume|abort","issuedUtc","by":"Dra. Teste"}],"serverTimeUtc"}`.
  O portal devolve todos os comandos ainda não confirmados; o headset confirma em `ackCommandIds` no envio seguinte
  e aplica cada comando uma só vez. `pause` equivale a segurar o botão (apresentação em curso descartada);
  `resume` só vale se o paciente está com o headset (senão fica aguardando o toque dele); `abort` = encerrar a sessão
  (o olho em andamento é salvo com `Aborted: true`).

## Portal

- **Headsets**: botão *Parear headset* gera o código e o `portal.json` para baixar; lista de tokens com revogação.
- **Pedido de exame**: campo *Enviar para o headset* (pré-selecionado quando há um só headset pareado); pedido pode ser cancelado.
- **Ao vivo** (`/live`): sessões ativas (último envio há menos de 30 s) e painel por sessão (`/live/<sessionId>`): estado,
  olho, paciente (código e nome, para quem tem acesso), mapa numérico preenchendo em dB equivalente HFA, última
  apresentação, contadores de confiabilidade com alerta, tempo, botões *Pausar / Retomar / Encerrar* (auditados).
  O navegador recebe os instantâneos por *Server-Sent Events* (`/api/live/<sessionId>/stream`).
- O estado ao vivo fica em memória no processo do portal e também em `LiveSession` (último instantâneo), para
  sobreviver a reinício e manter histórico.

## Por que HTTP + SSE e não WebSocket

A SPEC pede WebSocket "quando disponível". `next start` não aceita *upgrade* de WebSocket sem servidor próprio, e o
`ClientWebSocket` no IL2CPP/Android do Unity é instável. O envio por POST a 1 Hz com comandos na resposta dá o mesmo
painel ao vivo e o controle pausar/encerrar com atraso ≤ 1 s, sem dependência nova nos dois lados. Pode ser trocado
por WebSocket depois sem mudar o conteúdo dos instantâneos.

## Segurança e LGPD

- Só o código pseudônimo sai para o headset; nome e dados identificáveis ficam no portal.
- Em rede local use HTTPS quando possível (proxy reverso); o token em claro em HTTP numa LAN de consultório é um risco
  aceito nesta fase e documentado. Revogar = página *Headsets* ou apagar `portal-token.json`.
- O headset nunca recebe comandos além de pausar/retomar/encerrar; nenhum resultado é mostrado ao paciente.
