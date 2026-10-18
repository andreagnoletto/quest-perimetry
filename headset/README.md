# headset — Unity 6 (Meta Quest 3 / 3S)

> Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA

MVP da Fase 2: fluxo completo do paciente, 24-2 ZEST monocular com fixação central + Heijl-Krakau,
resposta pelo gatilho do controle (ou clicker Bluetooth), gravação local em JSON com HMAC.

## Estrutura

| Pasta | O quê | Compila onde |
|---|---|---|
| `Assets/Perimetry/Logic` | Fluxo do paciente, tempo das apresentações, geometria angular, dithering, gravação, cliente de sincronização com o portal (`PortalSync`, `LiveSnapshot`). C# puro (`noEngineReferences`). | Unity e `dotnet` (testes em `LogicTests/`) |
| `Assets/Perimetry/Runtime` | `PerimetryApp` (bootstrap), `HeadsetRig` (cena presa à cabeça), `HeadTracking`, `ResponseInput`, `ConfigLoader`, `PortalSyncBehaviour` (HTTP via UnityWebRequest). | Unity (checagem de compilação em `UnityCompileCheck/`) |
| `Assets/Perimetry/Resources/Perimetry` | Shaders `Perimetry/MonocularUnlit` e `Perimetry/MonocularText` (roteamento por olho + dithering). | Unity |
| `Assets/Perimetry/Editor` | `Perimetry > Configure Project`, `Perimetry > Sync Config` (copia `config/` para Resources antes de cada build). | Unity |
| `Assets/Perimetry/Tests/EditMode` | Teste de tamanho angular pelo rig real, hash da config, shaders presentes. | Unity Test Runner |
| `LogicTests/` | 37 testes NUnit da lógica, incluindo exames completos simulados quadro a quadro e a sincronização contra um portal simulado. | `dotnet test headset/LogicTests` |

A biblioteca `/core` entra como pacote local (`Packages/manifest.json` → `file:../../core/Perimetry.Core`),
então o headset roda exatamente o mesmo código psicofísico testado na Fase 1. As constantes clínicas vêm
de `config/` na raiz (copiadas para `Assets/Resources/PerimetryConfig`, gerado e fora do git); o hash da
config no exame do headset é o mesmo do CLI.

## Primeira abertura (na sua máquina)

1. Unity Hub → instalar **Unity 6 LTS (6000.0.x)** com **Android Build Support** (OpenJDK + SDK/NDK).
2. Abrir a pasta `headset/` como projeto. Se o Unity oferecer atualizar a versão do editor, aceite.
3. Menu **Perimetry > Configure Project**. Ele ajusta: espaço de cor Linear, URP sem HDR/MSAA/pós-processamento,
   OpenXR com Meta Quest feature e perfil Touch, single-pass instanced, Android ARM64/IL2CPP/Vulkan, min SDK 32,
   Active Input Handling = Both, e cria `Assets/Scenes/Perimetry.unity`. **Reinicie o editor** se o log pedir.
4. **Window > General > Test Runner > EditMode > Run All** (deve passar tudo).
5. Commitar os `.meta` gerados em `headset/Assets` (os do pacote `/core` ficam fora do git).

## Rodar no Meta XR Simulator

Instale o Meta XR Simulator (pacote `com.meta.xr.simulator` pelo registro da Meta, ou o instalador da Meta),
ative-o como runtime OpenXR (menu **Meta > Meta XR Simulator > Activate**) e dê Play na cena `Perimetry`.
No simulador: gatilho do controle = resposta; no teclado, Espaço/Enter também respondem, setas ajustam as
telas do operador e Esc segurado 3 s encerra a sessão.

## APK para o Quest 3

**File > Build Profiles > Android > Switch Platform**, depois **Build**. Instale com
`adb install -r QuestPerimetry.apk` (Quest em modo desenvolvedor). Roda sem rede.

Arquivos no headset (`/sdcard/Android/data/br.questperimetry.headset/files/`):

| Arquivo | Uso |
|---|---|
| `exam-order.json` (opcional) | `{"patientRef":"P-001","ageYears":63,"refractiveCorrection":"+2,00 esf","presetId":"24-2-zest"}`. Sem ele, o operador escolhe o pedido vindo do portal (se pareado) ou exame e idade no headset. É renomeado após o uso. |
| `portal.json`, `portal-token.json` (opcionais) | Ligação com o portal do médico; veja *Sincronização com o portal* abaixo. |
| `calibration.json` (opcional) | Calibração fotométrica (`LuminanceCalibration`). Ausente ou vencida = relatório "NÃO CALIBRADO". |
| `exams/exam_*.json` + `.hmac` | Um registro por olho com todas as apresentações. `Aborted: true` se o operador interrompeu. |
| `exams/session_*.json` + `.hmac` | Log da sessão: verificações, treino, pausas, taxa de quadros, origem da posição dos olhos. |

Copie com `adb pull /sdcard/Android/data/br.questperimetry.headset/files/exams .` e analise com o CLI
(`perimetry analyze`) ou o script de relatório.

## Sincronização com o portal (Fase 4)

Opcional e **offline-first**: o exame nunca espera a rede. Tudo é gravado em `exams/` como antes e enviado
depois; sem `portal.json`/`portal-token.json` o comportamento é exatamente o da Fase 2. Contrato completo em
[`docs/SYNC_API.md`](../docs/SYNC_API.md).

**Parear (uma vez por headset):**

1. No portal, página *Headsets e calibração* → *Parear headset*. O portal gera um código (vale 15 min) e o arquivo
   `portal.json` já com o endereço do portal na rede local.
2. Copie para o headset: `adb push portal.json /sdcard/Android/data/br.questperimetry.headset/files/`.
3. Abra o app com o Quest na mesma rede. Em poucos segundos ele troca o código pelo token (`portal-token.json`),
   renomeia `portal.json` para `portal.used-<data>.json` e envia a chave `device.key`, então os exames chegam ao
   portal com integridade **verificada**. Para desparear, revogue no portal ou apague `portal-token.json`.

**No dia a dia:**

- Ao abrir, o headset busca os pedidos de exame enviados a ele. Depois do toque na tela de boas-vindas, o operador
  vê o pedido (código do paciente · exame · idade); o direcional alterna entre pedidos e a opção *sem pedido (manual)*;
  segurar o gatilho confirma. Aguarde a tela de boas-vindas uns 2 s antes de tocar, para os pedidos chegarem.
  Um `exam-order.json` presente tem prioridade e pula essa tela.
- Durante a sessão o headset envia um instantâneo por segundo (estado, mapa parcial, confiabilidade) para o painel
  *Ao vivo* do portal, e aplica os comandos *pausar / retomar / encerrar* do médico. Pausar remoto = o paciente
  segurar o botão (apresentação em curso descartada, pausa registrada com motivo `operator`); retomar só vale com o
  headset na cabeça; encerrar grava o olho em andamento com `Aborted: true`.
- Ao terminar, os arquivos da sessão (`exam_*.json`, `session_*.json` e seus `.hmac`) são enviados; cada arquivo
  aceito ganha um recibo `<arquivo>.sent`. O que não foi aceito (ou foi gravado sem rede) fica na fila e é
  reenviado na próxima abertura do app. Falhas de rede só adiam a próxima tentativa (`sync.retryBackoffMs`).
- Configuração em `config/headset.json` → `sync` (`enabled`, `liveIntervalMs`, `requestTimeoutMs`, `retryBackoffMs`,
  `maxFilesPerUpload`). O token em claro numa LAN HTTP é um risco aceito nesta fase; use HTTPS (proxy reverso) quando possível.

## Controles

- **Paciente**: qualquer gatilho (ou clicker). Toque = resposta/avançar. Segurar 1,5 s durante o exame = pausa.
- **Operador**: direcional (ou setas) ajusta pedido/exame/idade; segurar o gatilho confirma; segurar B/Y (ou Esc) 3 s
  encerra a sessão (o olho em andamento é salvo com `Aborted: true`). Pausar, retomar e encerrar também pelo painel *Ao vivo* do portal.
- Tirar o headset pausa o exame; ao recolocar, o paciente aperta para continuar.

## Limitações conhecidas desta fase

- Validado aqui só fora do Unity (lógica + compilação contra a API do UnityEngine). Shaders, scripts de editor,
  simulador e APK precisam ser verificados no Unity 6.
- O Quest começa a 72 Hz: 200 ms viram 14 quadros (194 ms). O pedido de 90 Hz (18 quadros = 200 ms) depende
  da API de display da Meta/OpenXR e ainda não está ligado; a taxa real é medida e gravada no log.
- Timestamps de resposta têm resolução de um quadro (~11–14 ms) e incluem a latência fixa do display.
- Áudio de voz: só o gancho (`Resources/Perimetry/Voice/<idioma>/<chave>`), sem gravações ainda.
- Sincronização: os pedidos do portal só aparecem se chegarem enquanto a tela de boas-vindas está aberta; o endereço
  do portal entra por arquivo (`portal.json` via adb), não há teclado no headset; `UnityWebRequest` e o pareamento
  precisam ser verificados no Quest real (aqui só a lógica e a compilação contra a API do UnityEngine).
