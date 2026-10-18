# headset — Unity 6 (Meta Quest 3 / 3S)

> Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA

MVP da Fase 2: fluxo completo do paciente, 24-2 ZEST monocular com fixação central + Heijl-Krakau,
resposta pelo gatilho do controle (ou clicker Bluetooth), gravação local em JSON com HMAC.

## Estrutura

| Pasta | O quê | Compila onde |
|---|---|---|
| `Assets/Perimetry/Logic` | Fluxo do paciente, tempo das apresentações, geometria angular, dithering, gravação. C# puro (`noEngineReferences`). | Unity e `dotnet` (testes em `LogicTests/`) |
| `Assets/Perimetry/Runtime` | `PerimetryApp` (bootstrap), `HeadsetRig` (cena presa à cabeça), `HeadTracking`, `ResponseInput`, `ConfigLoader`. | Unity (checagem de compilação em `UnityCompileCheck/`) |
| `Assets/Perimetry/Resources/Perimetry` | Shaders `Perimetry/MonocularUnlit` e `Perimetry/MonocularText` (roteamento por olho + dithering). | Unity |
| `Assets/Perimetry/Editor` | `Perimetry > Configure Project`, `Perimetry > Sync Config` (copia `config/` para Resources antes de cada build). | Unity |
| `Assets/Perimetry/Tests/EditMode` | Teste de tamanho angular pelo rig real, hash da config, shaders presentes. | Unity Test Runner |
| `LogicTests/` | 25 testes NUnit da lógica, incluindo exames completos simulados quadro a quadro. | `dotnet test headset/LogicTests` |

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
| `exam-order.json` (opcional) | `{"patientRef":"P-001","ageYears":63,"refractiveCorrection":"+2,00 esf","presetId":"24-2-zest"}`. Sem ele, o operador escolhe exame e idade no headset. É renomeado após o uso. |
| `calibration.json` (opcional) | Calibração fotométrica (`LuminanceCalibration`). Ausente ou vencida = relatório "NÃO CALIBRADO". |
| `exams/exam_*.json` + `.hmac` | Um registro por olho com todas as apresentações. `Aborted: true` se o operador interrompeu. |
| `exams/session_*.json` + `.hmac` | Log da sessão: verificações, treino, pausas, taxa de quadros, origem da posição dos olhos. |

Copie com `adb pull /sdcard/Android/data/br.questperimetry.headset/files/exams .` e analise com o CLI
(`perimetry analyze`) ou o script de relatório.

## Controles

- **Paciente**: qualquer gatilho (ou clicker). Toque = resposta/avançar. Segurar 1,5 s durante o exame = pausa.
- **Operador**: direcional (ou setas) ajusta exame/idade; segurar o gatilho confirma; segurar B/Y (ou Esc) 3 s
  encerra a sessão (o olho em andamento é salvo com `Aborted: true`).
- Tirar o headset pausa o exame; ao recolocar, o paciente aperta para continuar.

## Limitações conhecidas desta fase

- Validado aqui só fora do Unity (lógica + compilação contra a API do UnityEngine). Shaders, scripts de editor,
  simulador e APK precisam ser verificados no Unity 6.
- O Quest começa a 72 Hz: 200 ms viram 14 quadros (194 ms). O pedido de 90 Hz (18 quadros = 200 ms) depende
  da API de display da Meta/OpenXR e ainda não está ligado; a taxa real é medida e gravada no log.
- Timestamps de resposta têm resolução de um quadro (~11–14 ms) e incluem a latência fixa do display.
- Áudio de voz: só o gancho (`Resources/Perimetry/Voice/<idioma>/<chave>`), sem gravações ainda.
