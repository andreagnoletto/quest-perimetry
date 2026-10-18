# Especificação de Requisitos de Software (SRS) — Perimetria Computadorizada em Meta Quest

> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.**
> Este documento não afirma nem implica aprovação regulatória de nenhuma autoridade. É a primeira versão
> (Fase 6) da documentação de requisitos, derivada do que está implementado e da especificação do product
> owner clínico (`SPEC.md`), para servir de base a um futuro dossiê.

Versão 0.1 · 2026-10-18 · Fase 6 · Autor: equipe de desenvolvimento, revisão pendente do product owner clínico.

## 1. Escopo e uso pretendido (rascunho)

Sistema de perimetria estática automatizada (SAP) acromática em headset de realidade virtual (Meta Quest 3 /
3S; Quest Pro para eye-tracking), composto por:

- **`/core`** — biblioteca de psicofísica e análise (C# puro), usada pelo headset e pelo portal;
- **`/headset`** — aplicativo Unity que apresenta os estímulos e conduz o paciente;
- **`/portal`** — portal web do médico (Next.js + PostgreSQL) para cadastro, pedidos, importação, laudo
  no leiaute *Single Field Analysis*, painel ao vivo e exportações.

**Uso pretendido (rascunho, a confirmar pelo product owner):** medir e documentar a sensibilidade luminosa
diferencial do campo visual central (até 30°) de pacientes adultos, sob supervisão de oftalmologista, para
pesquisa e apoio à avaliação clínica; o sistema não emite diagnóstico. Limitação intrínseca: a luminância
máxima do display (~100 cd/m²) restringe a faixa mensurável a cerca de 15,5 dB equivalente HFA ou mais
(`docs/CLINICAL_SPEC.md` §2).

## 2. Convenções

- Identificador `REQ-<ÁREA>-<nnn>`. Áreas: STIM (estímulo/luminância), STRAT (estratégias), REL
  (confiabilidade/fixação), ANA (análise/índices), REC (registros/auditoria), HS (fluxo no headset), SYNC
  (sincronização), PORT (portal), SEC (segurança/LGPD), DOC (rotulagem/documentação).
- **Fonte**: seção da `SPEC.md` (S§) ou de `docs/CLINICAL_SPEC.md` (C§) de onde o requisito deriva.
- **Status**: *implementado*; *proposto* = implementado com padrão proposto, aguardando confirmação do
  product owner; *planejado* = Fase 6 ou posterior, ainda sem implementação. Itens
  **[DECISÃO PENDENTE]** aguardam decisão clínica e estão listados também em `docs/CLINICAL_SPEC.md`.
- **Config**: chave em `config/*.json` quando o valor é uma constante clínica (SPEC §0.4). O hash desse
  diretório vai em cada exame (REQ-REC-002).
- A verificação de cada requisito está em `TRACEABILITY.md` (gerada por `tools/trace/build_matrix.py`
  a partir de `traceability.json`, que usa os mesmos identificadores).

## 3. Requisitos

### 3.1 STIM — estímulo, fundo e escala de luminância

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-STIM-001 | O fundo do campo é de 10 cd/m² (31,5 asb); a luminância máxima nominal é 100 cd/m² até existir calibração fotométrica. | S§2, C§2 | implementado | `display.json › backgroundCdm2`, `maxLuminanceCdm2` |
| REQ-STIM-002 | O dB do dispositivo é definido sobre o incremento de luminância: dB = 10·log10(ΔLmax/ΔL), com 0 dB = estímulo mais brilhante do dispositivo. | S§2, C§2 | implementado | — |
| REQ-STIM-003 | O dB equivalente HFA é exposto apenas como transformação (offset = 10·log10(ΔLmax,HFA/ΔLmax)); o mínimo mensurável é indicado e valores abaixo dele saem como "<NN". | S§2, C§2 | implementado | `display.json › hfaMaxAsb` |
| REQ-STIM-004 | Uma tabela de calibração fotométrica por olho (nível → cd/m², com serial, fotômetro, data e validade) pode ser carregada; sem ela usa-se a curva sRGB nominal e todo exame e relatório leva "NÃO CALIBRADO". | S§2, C§2 | implementado | arquivo de calibração (`LuminanceCalibration`) |
| REQ-STIM-005 | O estímulo padrão é Goldmann III (0,431°), com tamanhos I a V configuráveis. | S§2, C§3 | implementado | `stimulus.json › size` |
| REQ-STIM-006 | Todo estímulo é posicionado em coordenadas angulares numa esfera centrada no olho testado, nunca em pixels; o erro do tamanho angular é < 5 % em qualquer posição do campo. | S§2, S§11, C§1, C§11 | implementado | `display.json › virtualStimulusDistanceM` |
| REQ-STIM-007 | O estímulo dura 200 ms, quantizados em quadros inteiros da taxa de atualização medida; a taxa e o número de quadros são registrados. **[DECISÃO PENDENTE]**: duração efetiva a 72 Hz (194 ms). | S§2, C§3, C§11 | implementado | `stimulus.json › durationMs` |
| REQ-STIM-008 | A renderização trabalha em espaço linear com dithering para que a luminância média de cada nível fique a ≤ 0,5 dB do pedido entre 0 e 30 dB do dispositivo. | S§2, C§11 | implementado | `display.json` |
| REQ-STIM-009 | O teste é monocular: o olho não testado recebe tela preta durante verificações, treino e exame. | S§2, C§11 | implementado | `display.json › untestedEyeLuminanceCdm2` |
| REQ-STIM-010 | As grades 24-2 (54), 30-2 (76), 10-2 (68), 24-2C (64) e uma grade CSV customizada são dados versionados, armazenadas na convenção do OD, espelhadas para o OS, com os pontos da mancha cega em (15°, ±3°) excluídos dos índices. | S§4, C§4 | implementado | `config/grids/*.json` |
| REQ-STIM-011 | Toda grade oferecida cabe no campo útil de ±50° do Quest 3; a 60-4 não é oferecida. | S§2, C§4 | implementado | `display.json › usableHalfFieldDeg` |

### 3.2 STRAT — estratégias de limiar e simulação

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-STRAT-001 | O ZEST é um porte do `OPI::ZEST` com os mesmos parâmetros: domínio −5..35 dB, verossimilhança fpr + (1−fpr−fnr)·(1−Φ((s−t)/σ)), prior bimodal 4:1 (Turpin 2003), estímulo = média da posterior. | S§5, C§6.1 | implementado | `strategies.json › zest` |
| REQ-STRAT-002 | O ZEST para quando o DP da posterior é < 1,5 dB (padrão, aprovado 2026-10-17; 2,0 dB no ZEST rápido), ou após 2 "não visto" a 0 dB, 2 "visto" a 30 dB ou 100 apresentações. | C§6.1 | implementado | `strategies.json › zest.stopSd`, `zestFast.stopSd` |
| REQ-STRAT-003 | O Full Threshold 4-2 é um porte do `OPI::FT`: passos de 4 dB até a 1ª reversão e 2 dB até a 2ª, limiar = último visto, segunda escada se o resultado difere > 4 dB do início. | S§5, C§6.2 | implementado | `strategies.json › fullThreshold` |
| REQ-STRAT-004 | O rastreio supraliminar apresenta 6 dB acima do esperado para a idade, retesta uma vez os não vistos e não faz tentativas de falso negativo (aprovado 2026-10-17). | S§5, C§6.3 | implementado | `strategies.json › screening` |
| REQ-STRAT-005 | O padrão de crescimento inicia pelos 4 pontos-âncora; vizinhos partem do esperado deslocado pela média do desvio dos vizinhos terminados; pontos isolados usam o desvio global. | S§5, C§6 | implementado | `strategies.json › growth` |
| REQ-STRAT-006 | Os portes de ZEST, 4-2 e FT produzem, nos mesmos cenários simulados, a mesma sequência de estímulos e o mesmo resultado que o OPI em R (cross-check automatizado). | S§10, C§10 | implementado | — |
| REQ-STRAT-007 | Para uma mesma semente, um exame produz a mesma sequência de apresentações e o mesmo resultado (PRNG próprio, nunca `System.Random`). | S§0.6 | implementado | — |
| REQ-STRAT-008 | Em simulação com observador normal, o 24-2 ZEST tem erro absoluto médio < 2 dB e o ZEST rápido dura < 5 min; um defeito arqueado sintético é detectado com PD p < 1 % nos pontos corretos. | S§5, S§11 | implementado | — |
| REQ-STRAT-009 | O simulador de paciente modela a função psicométrica (Henson ou Gaussiana), falsos positivos/negativos, perdas de fixação, mancha cega fisiológica e campos sintéticos (normal, arqueado, hemianopsia, escotoma central). | S§5, C§9 | implementado | — |
| REQ-STRAT-010 | O limiar foveal é opcional e desligado por padrão; quando ligado, é medido antes da mancha cega, com alvo em losango de 4 pontos e a estratégia do exame, e não entra nos índices. | S§4, C§4 | proposto | `strategies.json › foveal` |
| REQ-STRAT-011 | A escolha forçada é experimental e desligada por padrão: N = 2 intervalos de 200 ms com marcador, acerto = visto, piso da verossimilhança do ZEST = 1/N, sem FP nem Heijl-Krakau, sem regra de antecipação; opção escolhida pelo médico no pedido (decisão 2026-10-18). **[DECISÃO PENDENTE]**: N e critério de parada. | S§5, C§6.4 | proposto | `strategies.json › forcedChoice` |

### 3.3 REL — confiabilidade e fixação

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-REL-001 | No modo A (padrão), a mancha cega é localizada antes do teste (0 dB em (15°, −1,5°) e 12 vizinhas) e um estímulo de 0 dB é apresentado nela a cada 30 ± 6 apresentações; resposta = perda de fixação (Heijl-Krakau). | S§3, S§6, C§5, C§7 | implementado | `reliability.json › fixationLoss` |
| REQ-REL-002 | Após 2 perdas de fixação consecutivas a mancha cega é remapeada antes de contar novas perdas (aprovado 2026-10-18); 0 desliga. | C§5 | implementado | `reliability.json › fixationLoss.remapAfterConsecutiveLosses` |
| REQ-REL-003 | Se a mancha cega não for localizada, o monitoramento de fixação é desativado e o exame e o relatório avisam. | C§5 | implementado | — |
| REQ-REL-004 | Falsos positivos são medidos por tentativas sem estímulo a cada 30 ± 6 apresentações; alerta acima de 15 %. | S§6, C§7 | implementado | `reliability.json › falsePositive` |
| REQ-REL-005 | Falsos negativos são medidos reapresentando 9 dB acima do limiar medido (só pontos ≥ 9 dB) a cada 40 ± 8 apresentações; alerta acima de 33 %. | S§6, C§7 | implementado | `reliability.json › falseNegative` |
| REQ-REL-006 | A janela de resposta é de 1000 ms e o intervalo entre estímulos é aleatório entre 600 e 1200 ms; resposta < 100 ms é descartada e refeita, resposta tardia conta como não visto; nenhuma apresentação descartada altera a estratégia. | S§2, C§3 | implementado | `timing.json` |
| REQ-REL-007 | Com eye-tracking, uma apresentação de limiar cujo olhar está > 3° do alvo no início do estímulo é descartada e retestada; o traçado de olhar é gravado por apresentação. | S§3, C§5 | proposto | `reliability.json › gaze` |
| REQ-REL-008 | No modo B (oculocinético), o alvo salta para uma posição aleatória até ±10° antes de cada estímulo, o estímulo é composto em relação ao alvo (grade retiniana preservada, excentricidade absoluta ≤ 45°) e o travamento dura 600 ms ou até o olhar ficar ≤ 3° por 300 ms. | S§3, C§5 | proposto | `reliability.json › oculokinetic` |
| REQ-REL-009 | O modo sem monitoramento de fixação não apresenta estímulos de mancha cega e é declarado no exame. | C§5 | implementado | — |
| REQ-REL-010 | Segurar o botão 1500 ms, retirar o headset ou um comando do portal pausa o exame: a apresentação em curso é descartada sem entrar nas contagens e o intervalo fica registrado. **[DECISÃO PENDENTE]**: tempo de pausa. | S§8, C§11 | implementado | `timing.json › pauseHoldMs`, `resumeAfterPauseDelayMs` |
| REQ-REL-011 | Todo exame e relatório traz perdas de fixação, FP, FN (com marca de alerta acima do limite), duração, número de apresentações, modo de fixação, calibração e normativo usados. | S§6, C§7 | implementado | `reliability.json › *.alertLimit` |

### 3.4 ANA — análise e índices

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-ANA-001 | O normativo é um plug-in de dados por grade (idade × ponto → esperado, DP, percentis), construído de uma população de campos normais como em `visualFields::nvgenerate`. | S§7, C§8.1 | implementado | `config/normative/` |
| REQ-ANA-002 | O normativo provisório sintético é marcado PROVISÓRIO; enquanto for usado, todo exame, relatório e mapa de probabilidade leva o aviso "NORMATIVO PROVISÓRIO". | S§7, C§8.1 | implementado | `normative/provisional-synthetic.json` |
| REQ-ANA-003 | O desvio total, a altura geral (percentil 85 dos TD) e o desvio padrão (pattern) são calculados por ponto em dB equivalente HFA; "<0 dB" é analisado como 0. | S§7, C§8.2 | implementado | — |
| REQ-ANA-004 | O MD é a média dos TD ponderada pela variância inversa (Heijl 1987); a ponderação 1/DP é opção. | S§7, C§8.2 | implementado | `analysis.json › mdWeights` |
| REQ-ANA-005 | O PSD segue Heijl 1987 e é zero para depressão uniforme. | S§7, C§8.2 | implementado | — |
| REQ-ANA-006 | O VFI segue Bengtsson & Heijl 2008 (ponderação por magnificação cortical) e é mostrado com uma casa decimal (decisão 2026-10-18). | S§7, C§8.2, C§12 | implementado | — |
| REQ-ANA-007 | O GHT segue Åsman & Heijl 1992 com as cinco saídas; nas grades com `ghtApplicable: false` (10-2) o resultado é "Não se aplica". **[DECISÃO PENDENTE]**: composição dos setores (`config/ght.json`, TO_BE_VALIDATED). | S§7, C§8.2 | implementado | `ght.json`, `grids/*.json › ghtApplicable` |
| REQ-ANA-008 | A escala de cinza tem 10 faixas de 4 dB sobre o valor equivalente HFA (< 4 dB preto … ≥ 36 dB branco; aprovado 2026-10-17). | S§7, C§8.2 | implementado | `grayscale.json` |
| REQ-ANA-009 | Os mapas de probabilidade de TD e PD usam os níveis p < 5 %, 2 %, 1 % e 0,5 % com os símbolos do HFA. | S§7, C§8.2, C§12 | implementado | `analysis.json › probabilityLevels` |
| REQ-ANA-010 | Um exame interrompido (`Aborted`) nunca é analisado como campo completo; arquivos inválidos são recusados com erro. | C§11, C§12 | implementado | — |
| REQ-ANA-011 | O limiar foveal é comparado ao esperado foveal para a idade (TD e p) e não entra em MD, PSD, VFI nem GHT. | C§4, C§8.1 | proposto | `normative/provisional-synthetic.json › fovealExtraDb` |
| REQ-ANA-012 | A tendência é a regressão linear de MD e VFI contra o tempo (inclinação/ano, erro-padrão, p pela t com n−2 gl), excluindo exames interrompidos e rastreios; não há análise por eventos (GPA). | S§7, C§12 | implementado | — |
| REQ-ANA-013 | As funções estatísticas (CDF e quantil da normal, quantil tipo 7) reproduzem o R. | S§10 | implementado | — |
| REQ-ANA-014 | O portal oferece ferramenta para coletar o normativo próprio (coorte de voluntários saudáveis, critérios de exclusão, estatística por faixa etária, export) e substituir o provisório pelo medido: toda regra e estatística no `/core`, um normativo ativo por grade, reanálise registrada no log, relatório com o normativo usado. | S§7, S§9, C§8.1, C§12 | implementado (regras aprovadas em 2026-10-18) | `normative/cohort.json` |

### 3.5 REC — registros, auditoria e exportação

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-REC-001 | Todo exame grava cada apresentação: localização, intensidade, tempo, resposta, latência, estado da fixação e disposição, para auditoria e reprocessamento offline. | S§0.6, C§7 | implementado | — |
| REQ-REC-002 | O hash SHA-256 do diretório `config/` é gravado em todo exame e muda quando qualquer arquivo de configuração muda. | S§0.4, C§0 | implementado | — |
| REQ-REC-003 | Os arquivos gravados pelo headset são assinados com HMAC por dispositivo; a alteração de um byte é detectada no headset e na importação. | S§1, C§12 | implementado | chave em `device.key` |
| REQ-REC-004 | A análise é entregue em JSON estrito (`perimetry-report/1`) pelo `/core`; o portal não calcula nenhum número clínico. | C§12 | implementado | — |
| REQ-REC-005 | As configurações do exame (grade, estratégia, fixação, foveal, modo de resposta, semente) ficam no registro e são reconstruíveis. | S§0.6 | implementado | — |
| REQ-REC-006 | O log de sessão do headset registra modelo, serial, versão do software, taxa de atualização medida, origem dos olhos e os eventos do fluxo. | S§0.6, C§11 | implementado | — |
| REQ-REC-007 | Cada exame pode ser exportado em PDF A4, CSV de pontos, JSON de análise e arquivo bruto original. | S§7 | implementado | — |
| REQ-REC-008 | O relatório avisa quando o hash da configuração do exame difere do da análise. | C§12 | implementado | — |

### 3.6 HS — fluxo no headset

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-HS-001 | O fluxo do paciente segue a SPEC §8: boas-vindas → exame/idade (ou pedido) → instruções → por olho: apresentação, nitidez, (oclusão), treino → exame → descanso → "Exame concluído. Aguarde o médico." | S§8, C§11 | implementado | `headset.json` |
| REQ-HS-002 | Antes do primeiro olho há treino com estímulos e feedback, repetido uma vez se o paciente erra demais. **[DECISÃO PENDENTE]**: estímulos do treino. | S§8, C§11 | implementado | `headset.json › training` |
| REQ-HS-003 | A verificação de oclusão do olho não testado é opcional e desligada por padrão (aprovado 2026-10-18); quando ligada, a falha chama o operador. | S§8, C§11 | implementado | `headset.json › checks.occlusionCheckEnabled` |
| REQ-HS-004 | Nenhuma tela do headset mostra ao paciente resultado, limiar ou índice. | S§8 | implementado | — |
| REQ-HS-005 | As mensagens ao paciente existem em pt-BR e en-US e o áudio é entregue uma vez por mensagem. | S§0.5, S§8 | implementado | `headset.json › language` |
| REQ-HS-006 | A interrupção pelo operador grava o olho em andamento com `Aborted = true`; uma falha de gravação é mostrada ao operador. | C§11 | implementado | — |
| REQ-HS-007 | O headset carrega o mesmo `config/` versionado, valida-o e seus presets referenciam grades e estratégias existentes; o preset padrão continua 24-2 ZEST modo A. | S§0.4, C§11 | implementado | `headset.json › presets` |
| REQ-HS-008 | Um pedido de exame (arquivo ou portal) define preset, idade, correção e semente; as opções da Fase 5 sobrescrevem o preset; um pedido com preset ou grade desconhecidos é recusado e registrado. | S§8, C§11, SYNC_API | implementado | — |
| REQ-HS-009 | O alvo de fixação é o losango central ou, para a fóvea, um losango de 4 pontos, conforme o pedido de apresentação do `/core`. **[DECISÃO PENDENTE]**: aparência do alvo. | S§3, C§11 | proposto | `headset.json › fixationTarget` |
| REQ-HS-010 | Na escolha forçada o headset executa marcador, intervalos, intervalo entre eles e janela, e atribui o aperto ao intervalo em curso. | C§6.4, C§11 | proposto | `strategies.json › forcedChoice` |
| REQ-HS-011 | O instantâneo ao vivo é gerado no headset a partir do `ExamSession` (inclusive os campos da Fase 5); nada é calculado no portal. | S§8, C§12a | implementado | `headset.json › sync.liveIntervalMs` |

### 3.7 SYNC — sincronização headset ↔ portal

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-SYNC-001 | O exame nunca depende da rede: sem `portal.json` ou com a sincronização desligada o fluxo é idêntico e nada é transmitido. | S§1, C§12a | implementado | `headset.json › sync.enabled` |
| REQ-SYNC-002 | O pareamento usa código de 8 caracteres sem letras ambíguas, válido 15 min e 5 tentativas; o headset para após recusa. | SYNC_API | implementado | — |
| REQ-SYNC-003 | O token do dispositivo tem 32 bytes aleatórios, é guardado só como SHA-256 e é revogável; um headset revogado para de sincronizar até parear de novo. | S§1, SYNC_API | implementado | — |
| REQ-SYNC-004 | Os exames gravados localmente são enviados com recibo (`.sent`) e fila com repetição e recuo; um erro mantém o arquivo na fila. | S§1, SYNC_API | implementado | `headset.json › sync.retryBackoffMs` |
| REQ-SYNC-005 | Os instantâneos ao vivo são validados pelo portal contra a forma documentada e entregues ao navegador por SSE; uma sessão é ativa quando enviou há < 30 s. | S§8, SYNC_API | implementado | — |
| REQ-SYNC-006 | Os comandos remotos limitam-se a pausar, retomar e encerrar; o headset confirma cada um uma única vez e o portal os audita. | S§8, C§12a | implementado | — |
| REQ-SYNC-007 | Só o código pseudônimo, a idade, a correção e as opções do exame saem do portal para o headset. | S§8, SYNC_API | implementado | — |
| REQ-SYNC-008 | Toda chamada `/api/headset/*` entra no log de acesso como `headset.*` com o serial do dispositivo. | SYNC_API | implementado | — |

### 3.8 PORT — portal do médico

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-PORT-001 | O cadastro de paciente guarda nome, nascimento, ID interno, sexo, refração e diagnóstico, registra consentimento e permite busca por índice cego exato. | S§8 | implementado | — |
| REQ-PORT-002 | A importação (manual ou pela rede) analisa cada exame pelo `/core`, verifica a integridade, vincula pelo código pseudônimo e deduplica. | S§8, C§12 | implementado | — |
| REQ-PORT-003 | O laudo segue o leiaute *Single Field Analysis* (cabeçalho, confiabilidade, parâmetros, mapas, GHT/VFI/MD/PSD), idêntico em tela e PDF A4. | S§7, S§11, C§12 | implementado | — |
| REQ-PORT-004 | O laudo traz os rótulos obrigatórios (protótipo/ANVISA, NÃO CALIBRADO, normativo provisório, confiabilidade, integridade) em nota de rodapé (decisão 2026-10-18). | S§0.7, C§12 | implementado | — |
| REQ-PORT-005 | Com eye-tracking, o rodapé traz a barra de rastreamento do olhar (uma marca por apresentação, limite de 3°, descartadas para baixo); sem rastreamento ela não aparece. | S§7, C§12 | proposto | — |
| REQ-PORT-006 | A página de calibração recebe a tabela fotométrica (validada pelo `/core`), data, validade e serial do dispositivo. | S§8 | implementado | — |
| REQ-PORT-007 | O pedido de exame define padrão, estratégia, modo de fixação, limiar foveal, modo de resposta e olhos, e é enviado ao headset pareado ou baixado como arquivo. | S§8, C§12 | implementado | — |
| REQ-PORT-008 | O painel ao vivo mostra o mapa preenchendo, a última apresentação, a confiabilidade com alerta e os botões pausar/retomar/encerrar. | S§8, C§12a | implementado | — |
| REQ-PORT-009 | O histórico por paciente mostra a série de escalas de cinza e a tendência de MD e VFI; a visão geral de progressão por olho (série cronológica de escala de cinza e probabilidade do desvio total com índices e confiabilidade, tendência de MD e VFI) é imprimível em PDF e declara que não é análise por eventos. | S§7, C§12 | implementado | — |
| REQ-PORT-010 | Os números da interface médica seguem o formato pt-BR (vírgula decimal, sem zero negativo). | S§0.5 | implementado | — |
| REQ-PORT-011 | Os limiares são exibidos em dB equivalente HFA com "<NN" abaixo do mínimo mensurável; o dB do dispositivo permanece no CSV e no JSON. | C§12 | implementado | — |
| REQ-PORT-012 | O laudo mostra modelo e número de série do headset que gravou o exame. | pedido do product owner 2026-10-18 | implementado | — |
| REQ-PORT-013 | O portal oferece uma página de demonstração (animação em pt-BR) para o paciente ver antes de colocar o headset. | decisão 2026-10-18 | planejado (Fase 6) | — |

### 3.9 SEC — segurança e LGPD

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-SEC-001 | Os campos identificáveis do paciente são cifrados em repouso com AES-256-GCM e chave de servidor; o texto cifrado nunca se repete e alterações são rejeitadas. | S§8 | implementado | `PERIMETRY_DATA_KEY` (ambiente) |
| REQ-SEC-002 | A busca de paciente usa só índice cego (HMAC do texto normalizado, por campo). | S§8 | implementado | — |
| REQ-SEC-003 | O código pseudônimo (P-XXXXXX) usa alfabeto sem caracteres ambíguos e é o único identificador que sai do portal. | S§8 | implementado | — |
| REQ-SEC-004 | Todo acesso a dados de paciente, exportação, comando remoto e chamada do headset gera entrada no log de acesso (usuário, ação, entidade, data). | S§8 | implementado | — |
| REQ-SEC-005 | As senhas são guardadas com scrypt e têm comprimento mínimo; a verificação leva tempo constante mesmo para e-mail inexistente. | S§1 | implementado | — |
| REQ-SEC-006 | A sessão do portal usa cookie httpOnly/sameSite com token aleatório guardado só como hash e validade limitada; toda página e rota exige usuário ativo. | S§1 | implementado | `sessionTtlHours` |
| REQ-SEC-007 | O consentimento (data, responsável, nota) e o consentimento para pesquisa são registrados; a exportação de pesquisa é pseudonimizada (ID de estudo, dias desde o primeiro exame, sem datas nem texto livre) e inclui só quem consentiu. | S§8 | implementado | — |
| REQ-SEC-008 | Nenhum dado identificável do paciente é gravado ou exibido no headset (apenas código pseudônimo e idade). | S§8 | implementado | — |
| REQ-SEC-009 | O login e o pareamento limitam tentativas por conta e por origem (bloqueio temporário). | hardening Fase 6 | planejado (Fase 6) | — |
| REQ-SEC-010 | O portal envia cabeçalhos de segurança HTTP (CSP, HSTS, X-Frame-Options, Referrer-Policy) e exige HTTPS fora do ambiente de desenvolvimento; o token do headset em HTTP numa LAN é risco aceito e documentado até lá. | SYNC_API | planejado (Fase 6) | — |
| REQ-SEC-011 | Os direitos do titular (LGPD art. 18) são atendidos por exportação dos dados de um paciente e anonimização a pedido, ambas registradas no log de acesso. | S§8 | planejado (Fase 6) | — |
| REQ-SEC-012 | Existem procedimentos documentados de backup, restauração e rotação da chave de cifra. | hardening Fase 6 | planejado (Fase 6) | — |

### 3.10 DOC — rotulagem e documentação

| ID | Requisito | Fonte | Status | Config |
|---|---|---|---|---|
| REQ-DOC-001 | O rótulo "Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA" aparece em todo exame, relatório, README e interface, e nenhuma afirmação de aprovação regulatória é feita. | S§0.7 | implementado | — |
| REQ-DOC-002 | `docs/CLINICAL_SPEC.md` descreve toda a psicofísica implementada com referências e marca as decisões pendentes e aprovadas. | S§0.3 | implementado | — |
| REQ-DOC-003 | As licenças de terceiros estão registradas (OPI e visualFields portados sob Apache-2.0 com atribuição; nenhum código GPL). | S§10 | implementado | — |
| REQ-DOC-004 | A matriz de rastreabilidade requisito → implementação → teste é gerada e verificada na integração contínua. | S§9 (Fase 6) | implementado | — |
| REQ-DOC-005 | Toda constante clínica vive em `config/*.json` versionado, nunca no código. | S§0.4 | implementado | `config/` |

## 4. Requisitos não funcionais e de ambiente

- **Plataformas:** headset Android (Meta Quest 3/3S; Quest Pro para eye-tracking), Unity 6 com OpenXR;
  `/core` em netstandard2.1 para compilar no Unity e no .NET 8; portal Node 22 + PostgreSQL 16.
- **Desempenho:** 24-2 ZEST rápido < 5 min simulados (REQ-STRAT-008); painel ao vivo com atraso ≤ 1 s
  (envio a 1 Hz).
- **Cobertura de testes:** > 80 % na biblioteca psicofísica (SPEC §11; medida em CI com cobertura
  Cobertura).
- **Idiomas:** código e identificadores em inglês; interface em pt-BR com i18n para en-US.
- **Itens fora do escopo atual:** 60-4, flutuação de curto prazo, análise de progressão por eventos
  (GPA), exportação DICOM OPV (modelo de dados preparado, não implementado), aprovação regulatória.

## 5. Decisões clínicas pendentes

Consolidadas em `docs/CLINICAL_SPEC.md` (**[DECISÃO PENDENTE]**) e em `CLAUDE.md › Open decisions`:
composição dos setores do GHT; tempo de pausa; estímulos do treino; aparência do alvo de fixação;
duração a 72 Hz; padrões propostos da Fase 5 (losango foveal e `fovealExtraDb`, limite de olhar 3°,
oculocinético ±10° / 600 ms / 300 ms, N e parada da escolha forçada).
