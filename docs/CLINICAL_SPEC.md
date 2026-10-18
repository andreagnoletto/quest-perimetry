# Especificação clínica e psicofísica implementada

> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.**
> Documento-base para a futura documentação regulatória (requisitos, riscos e rastreabilidade em `docs/regulatory/`). Descreve o que está implementado
> em `/core` (Fase 1). Toda constante citada vive em `config/*.json`; o hash SHA-256 desse
> diretório é gravado em cada exame (`ExamRecord.ConfigHash`).

Estado: Fase 1 (biblioteca `/core`, simulação). Itens marcados **[DECISÃO PENDENTE]** aguardam o
product owner clínico; **[APROVADO 2026-10-17]** indica default confirmado por ele.

## 1. Sistema de coordenadas

- Posições em **graus de ângulo visual** a partir do ponto de fixação, vistas pelo paciente:
  +x = direita, +y = superior.
- As grades são armazenadas na **convenção do OD** (temporal = +x; mancha cega em (+15°, −1,5°)).
  Para o OS, x é espelhado (`FieldPoint.ForEye`). As requisições de apresentação já saem nas
  coordenadas do olho testado.
- O estímulo é posicionado numa esfera virtual centrada no olho (convenção de cúpula do HFA):
  direção = (sen az · cos el, sen el, cos az · cos el), com az = x e el = y. Nunca em pixels.

## 2. Display, luminância e escala de dB

| Parâmetro | Valor | Arquivo |
|---|---|---|
| Fundo | 10 cd/m² (31,5 asb, igual ao HFA) | `display.json` |
| Luminância máxima nominal | 100 cd/m² (até haver calibração) | `display.json` |
| Máximo do HFA (0 dB) | 10.000 asb = 10.000/π cd/m² | `display.json` |

**Definição de dB (aprovada):** sobre o **incremento** de luminância acima do fundo, como no HFA e no OPI:

```
dB_dispositivo = 10 · log10(ΔL_max / ΔL)      ΔL = L_estímulo − L_fundo,  ΔL_max = L_max − L_fundo
```

0 dB é o estímulo mais brilhante que o dispositivo produz. Com os valores nominais, ΔL_max = 90 cd/m².

**dB equivalente HFA (somente comparação, é uma transformação):**

```
dB_HFA_eq = dB_dispositivo + 10 · log10(ΔL_max_HFA / ΔL_max),   ΔL_max_HFA = 10.000/π ≈ 3.183 cd/m²
```

Com valores nominais o deslocamento é **≈ 15,5 dB**. Consequência clínica importante: **nenhuma
sensibilidade abaixo de ~15,5 dB HFA-equivalente pode ser medida** (o "<0 dB" do dispositivo).
Defeitos profundos ficam comprimidos: um ponto com limiar real de 5 dB HFA aparece como "<0"
(= 15,5 dB HFA-eq) e seu TD fica limitado a cerca de −15 dB. MD e VFI de campos com defeitos
profundos ficam, portanto, menos negativos que no HFA. Isso está de acordo com a literatura citada
na SPEC (Quest 2 / Pico Neo 3 com limiar mínimo ≈ 20 dB na escala HFA; Iowa-HMP) e só é atenuado
por displays mais brilhantes ou estímulos maiores.

**Calibração (`LuminanceCalibration`):** tabela por olho de nível de comando linear (0–1) →
cd/m² medida com fotômetro, com serial do dispositivo, fotômetro, data e validade. Interpolação
linear e inversa. Sem arquivo medido usa-se a curva sRGB nominal e **todo exame e relatório leva
"NÃO CALIBRADO"**. Passos de 1 dB perto do limiar (≈ 0,1 cd/m² sobre 10 cd/m²) são menores que um
código de 8 bits (verificado em teste), por isso o renderizador (Fase 2) trabalhará em float/linear
com dithering.

## 3. Estímulo e tempos

| Parâmetro | Valor | Arquivo |
|---|---|---|
| Tamanho | Goldmann III (4 mm² a 300 mm → **0,431°**); I–V configuráveis | `stimulus.json` |
| Duração | 200 ms | `stimulus.json` |
| Janela de resposta | 1.000 ms a partir do início do estímulo | `timing.json` |
| Intervalo entre estímulos | aleatório uniforme 600–1.200 ms, contado a partir da resposta ou do fim da janela | `timing.json` |
| Resposta antecipada | latência < 100 ms → descartada e a apresentação é refeita **[APROVADO 2026-10-17]** | `timing.json` |
| Resposta tardia | após a janela → "não visto" | — |

O tamanho angular é verificado automaticamente em teste (erro < 5% em qualquer posição do campo,
para todos os tamanhos): o disco de raio d·tan(θ/2) a 1 m é medido pelo ângulo entre bordas opostas.

## 4. Grades

Coordenadas idênticas às do pacote visualFields (`locmaps`), em `config/grids/*.json`:

| Grade | Pontos | Pontos de mancha cega (excluídos dos índices) |
|---|---|---|
| 24-2 | 54 (grade de 6° deslocada 3°, até 21°, + nasais (−27, ±3)) | (15, ±3) |
| 30-2 | 76 | (15, ±3) |
| 10-2 | 68 (grade de 2° deslocada 1°) | — |
| 24-2C | 64 (24-2 + 10 maculares) | (15, ±3) |
| Customizada | CSV `x,y[,blindSpot]` | definidos no CSV |

60-4 não é implementada (não cabe no campo útil de ±50° do Quest). Pontos-âncora: (±9°, ±9°) nas
grades de 6° e (±3°, ±3°) no 10-2. Vizinhos: pontos a ≤ 1,5 × espaçamento (inclui diagonais).
Cada grade declara `ghtApplicable`: falso no 10-2 (os setores do GHT são pontos do 24-2; os poucos pontos
coincidentes do 10-2 não formariam um GHT), e o relatório mostra "GHT: não se aplica" com MD, PSD e VFI normais.

**Limiar foveal (Fase 5, padrão proposto, aguardando confirmação):** opcional (`ExamSettings.FovealThreshold`,
**desligado por padrão**, como "Fovea: OFF/ON" do HFA). Quando ligado, é a primeira etapa do exame, antes da busca da
mancha cega: o alvo de fixação passa a ser um **diamante de 4 pontos** (`config/strategies.json › foveal`, meia-largura
1,5°), o paciente olha para o centro vazio e o estímulo (Goldmann III, 200 ms) aparece exatamente no centro. A
estratégia é a do exame (ZEST por padrão; no rastreio usa-se ZEST, pois o rastreio não mede limiar), com prior
centrado no esperado foveal para a idade. O resultado fica em `ExamRecord.Foveal` (dB do aparelho, censura, paradas) e
o relatório mostra **"Limiar foveal: NN dB"** em dB HFA-eq com o desvio em relação ao esperado e seu p
(`analysis.FovealTd`, `FovealTdP`). O valor foveal não entra em MD/PSD/VFI/GHT (como no HFA).

## 5. Fixação

- **Modo A, Heijl-Krakau (default):** antes do teste, a mancha cega é localizada com estímulos de
  0 dB em (15°, −1,5°) e em 12 posições vizinhas (passos de 1,5°/3°). A primeira posição não vista
  em 2 de 2 apresentações é aceita. Durante o teste, a cada ~30 apresentações (±6) um estímulo de
  0 dB é mostrado ali; resposta = perda de fixação. Se a mancha cega não for localizada, o
  monitoramento é desativado e o relatório avisa.
- **Remapeamento (aprovado em 2026-10-18):** após `remapAfterConsecutiveLosses` (default 2) perdas de
  fixação seguidas, a busca da mancha cega é refeita antes de contar novas perdas, porque o ponto
  aceito pode estar na borda da mancha cega (visto com latência normal e fixação boa; observado no
  primeiro exame real no Quest). As perdas já contadas permanecem; `BlindSpotRemaps` registra as
  buscas extras. Se a nova busca não localizar a mancha cega, o monitoramento é desativado como acima.
  0 desliga.
- **Modo A com eye-tracking (Fase 5, padrão proposto, aguardando confirmação):** o apresentador informa, a
  cada apresentação, se houve rastreamento (`GazeTracked`) e a distância do olhar ao alvo no início do estímulo
  (`GazeErrorDeg`; se faltar, o máximo durante o estímulo). Nos modos `CentralEyeTracking` e `Oculokinetic`, uma
  apresentação de limiar com erro **> 3°** (`config/reliability.json › gaze.maxErrorDeg`) recebe a disposição
  `gazeRejected`: não atualiza o algoritmo e o ponto é retestado (`retestRejected`). Toda apresentação rastreada
  gera uma amostra em `Reliability.GazeTrace` (sequência, instante, erro, rejeitada) — a **barra de gaze trace** do
  relatório, como no HFA — e `GazeMeanErrorDeg`/`GazeSamples` resumem. No modo Heijl-Krakau o traçado é gravado,
  mas a regra de rejeição não se aplica (a fixação é monitorada pela mancha cega). Um `FixationOk = false` do
  apresentador continua valendo em qualquer modo (disposição `fixationLost`).
- **Modo B (oculocinético, Fase 5, padrão proposto, aguardando confirmação):** `FixationMode.Oculokinetic`.
  Antes de cada apresentação o alvo salta para uma posição sorteada uniformemente em ±10° nos dois eixos
  (`oculokinetic.targetRangeDeg`), redesenhada enquanto o estímulo resultante ultrapassar 45° de excentricidade
  absoluta (`maxEccentricityDeg`). **Geometria:** as coordenadas de cada ponto da grade continuam sendo o
  deslocamento *retiniano* em relação ao alvo (`FieldX/FieldY`); o headset coloca o estímulo em
  `AngularGeometry.Compose(alvo, ponto)`: a direção do ponto é girada pela rotação de menor arco que leva o
  "para a frente" até o alvo (fórmula de Rodrigues, eixo = frente × alvo — a rotação que o olho faz, pela lei de
  Listing, para fixar o alvo). Assim a distância angular estímulo–alvo é exatamente a excentricidade do ponto,
  qualquer que seja a posição do alvo, e o conjunto testado no espaço retiniano é a grade escolhida (SPEC §3).
  `AngularGeometry.Relative` é a inversa (testes de ida e volta no `/core`). Sem eye-tracking, o alvo fica
  `lockDelayMs` = 600 ms na nova posição com a animação de travamento antes do estímulo; com eye-tracking, o olhar
  precisa ficar a ≤ 3° do alvo por 300 ms (`gazeConfirmDeg`, `gazeConfirmMs`). Heijl-Krakau continua ativo por
  padrão (`heijlKrakau: true`), com a busca e os estímulos de mancha cega também relativos ao alvo.

## 6. Estratégias de limiar

Todas são máquinas de estado por ponto (`start/step/stop/final`, como no OPI), intercaladas em ordem
aleatória sem repetir o mesmo ponto em seguida. O motor (`ExamSession`) decide cada apresentação e
interpreta cada resposta; o apresentador (headset ou simulador) só mostra o estímulo, como na
arquitetura cliente-servidor do OPI.

**Padrão de crescimento (growth pattern):** começam os 4 pontos-âncora com o valor esperado para a
idade. Quando um ponto termina, seus vizinhos ainda não iniciados começam com o valor esperado
deslocado pela média do desvio total dos vizinhos já terminados. Pontos sem vizinhos terminados
recebem o desvio médio global.

### 6.1 ZEST (modo bayesiano)

Porte linha a linha de `OPI::ZEST` (Turpin, Apache-2.0), com os mesmos nomes de parâmetros.

- Domínio: −5 a 35 dB do dispositivo, passo 1 dB; estímulos entre 0 e 30 dB.
- Verossimilhança (padrão do OPI): P(visto | estímulo s, limiar t) = fpr + (1 − fpr − fnr)·(1 − Φ((s − t)/σ)), com
  fpr = fnr = 0,03 e σ = 1 dB (opção "henson": σ = min(6, exp(−0,081·t + 3,27)) na escala HFA).
- Prior bimodal (Turpin et al. 2003): 4 × N(esperado, 3 dB) + 1 × N(8 dB HFA-eq, 6 dB) + piso de 0,1%.
- Estímulo seguinte = valor do domínio mais próximo da média da posterior; estimativa final = média.
- Parada: DP da posterior < 1,5 dB **(aprovado)**, ou 2 "não visto" a 0 dB, ou 2 "visto" a 30 dB,
  ou 100 apresentações.
- **ZEST rápido** (`zestFast`): DP < 2,0 dB e prior normal com DP 2,5 dB, disponível como opção. O padrão é o ZEST com DP < 1,5 dB **[APROVADO 2026-10-17]**.

**Diferença em relação ao SITA (proprietário, não copiado):** o SITA usa modelos de campo visual
normais e glaucomatosos com correlação espacial entre pontos, estimativa de falsos positivos pelo
tempo de resposta (sem tentativas-controle) e regras de parada baseadas em erro de medida. Aqui cada
ponto tem posterior independente; a informação espacial entra apenas pelo deslocamento do prior dos
vizinhos, e os falsos positivos são medidos com tentativas sem estímulo.

### 6.2 Full Threshold 4-2

Porte de `OPI::FT`: escada de 4 dB até a 1ª reversão, depois 2 dB até a 2ª; limiar = **último
visto**. Se o resultado difere do valor inicial em mais de 4 dB, uma 2ª escada começa no resultado e
o seu último visto é reportado (como no OPI, o "último resposta" da 1ª escada é mantido). Também está
disponível `fourTwo` (média das duas últimas apresentações), só para pesquisa.

### 6.3 Rastreio supraliminar

Estímulo 6 dB mais brilhante que o esperado para a idade (arredondado); "não visto" é retestado uma
vez. Resultado por ponto: visto / visto no reteste / não visto. Não há tentativas de falso negativo
no rastreio (não há limiar medido para referenciar o +9 dB) **[APROVADO 2026-10-17]**.

### 6.4 Escolha forçada

**Fase 5, opção experimental, desligada por padrão** (`ExamSettings.ResponseMode = ForcedChoice`;
`config/strategies.json › forcedChoice`). Cada apresentação tem N = 2 intervalos de 200 ms separados por 600 ms,
cada um anunciado por um piscar do alvo de 100 ms (`markerMs`); o estímulo preenche um intervalo sorteado
(`PresentationRequest.StimulusInterval`) e o paciente indica, depois do último intervalo, em qual ele apareceu
(`PresentationResult.RespondedInterval`). Acerto = visto; erro ou sem resposta = não visto; não há regra de
resposta antecipada. Consequências psicofísicas, todas implementadas e avisadas no relatório:
- no ZEST o piso da verossimilhança passa de fpr = 0,03 para o nível do acaso **1/N** (`zestGuessRate`), isto é,
  P(acerto | estímulo muito abaixo do limiar) = 0,5 com 2 intervalos;
- **não há tentativas de falso positivo** (não existe "sem estímulo" quando a resposta é obrigatória) nem
  **Heijl-Krakau** (um "visto" ao acaso em 50% das vezes tornaria a mancha cega e as perdas de fixação sem
  sentido): a fixação só é monitorada por eye-tracking; falsos negativos continuam (erro de intervalo com
  estímulo 9 dB mais brilhante);
- como cada resposta carrega menos informação, o exame fica **~3× mais longo** (simulação 24-2 ZEST rápido:
  ≈ 640 apresentações / 25 min contra ≈ 200 / 5 min em sim/não; com N = 4 intervalos cai, mas segue bem acima).
  Por isso é experimental: antes de usar em pacientes, decidir N e um critério de parada próprio
  **[DECISÃO PENDENTE]**.
Os limiares medidos assim não são comparáveis aos do HFA; o relatório traz o aviso.
**Decisão do product owner (2026-10-18):** a escolha forçada fica como opção que o médico marca no pedido do exame
(portal › novo pedido › "Modo de resposta"), pensada para repetir exames pouco confiáveis em sim/não; os exames de rotina
continuam em sim/não. O laudo mostra a duração e o número de apresentações para avaliar o custo na prática.

## 7. Confiabilidade

| Índice | Método | Frequência | Alerta |
|---|---|---|---|
| Perdas de fixação | Heijl-Krakau (estímulo 0 dB na mancha cega) | 1 a cada 30 ± 6 apresentações | > 20% |
| Falsos positivos | tentativa sem estímulo; resposta = FP | 1 a cada 30 ± 6 | > 15% |
| Falsos negativos | ponto já terminado, limiar − 9 dB (9 dB mais brilhante); sem resposta = FN. Só pontos com limiar ≥ 9 dB | 1 a cada 40 ± 8 | > 33% |

As frequências foram **[APROVADO 2026-10-17]** reduzidas de 1/15–1/20 para caber no critério de
tempo. Também são reportados: duração, número de apresentações, rejeições por olhar, respostas
antecipadas, posição da mancha cega, modo de fixação, calibração e normativo usados. Com eye-tracking
(Fase 5): `GazeTracked`, `GazeMeanErrorDeg`, `GazeSamples` e o traçado `GazeTrace` (§5); as rejeições por olhar
(`GazeRejectedPresentations`) somam as do apresentador e as da regra de 3°. Na escolha forçada não há FP nem
Heijl-Krakau (§6.4).
Flutuação de curto prazo: não implementada (opcional na SPEC).

## 8. Análise (Single Field Analysis)

### 8.1 Normativo

`INormativeDatabase` é um plug-in por grade (idade × ponto → esperado, DP, percentis). Ele é
construído por `NormativeBuilder` a partir de uma população de campos normais, seguindo
`visualFields::nvgenerate`: regressão linear sensibilidade ~ idade por ponto; TD; GH; PD; percentis
empíricos inferiores (0,5%, 1%, 2%, 5%) de TD e PD por ponto; distribuições empíricas de MD, PSD,
GH e das diferenças de setores do GHT. O mesmo construtor servirá para o normativo próprio (Fase 6).

**Normativo PROVISÓRIO SINTÉTICO** (`config/normative/provisional-synthetic.json`), em dB HFA-eq:
3.000 sujeitos sintéticos (20–85 anos, semente fixa) com
média = 33,0 − 0,17·excentricidade − 0,03·max(y, 0) − 0,07·(idade − 45),
mais deslocamento global (DP 0,9 dB), variação local entre sujeitos (DP 1,2 + 0,05·exc) e ruído de
medida teste-reteste (DP 1,3 + 0,02·exc). Os parâmetros são uma aproximação da literatura, não
dados medidos. Todo exame e relatório leva o aviso vermelho **"NORMATIVO PROVISÓRIO"**.

**Fóvea (Fase 5):** cada sujeito sintético recebe também um valor foveal = modelo em excentricidade 0 +
`fovealExtraDb` (1,5 dB, proposto: no HFA a fóvea costuma medir 1–3 dB acima dos pontos a 3°) + o mesmo deslocamento
global + variação local e ruído de medida (gerados num fluxo aleatório separado, de modo que os valores da grade são
idênticos aos da Fase 1). `NormativeBuilder` ajusta a mesma regressão por idade e os percentis inferiores
(`ExpectedFovealHfa`, `FovealTdProbability`); uma coorte real sem medida foveal produz um normativo sem fóvea e, nesse
caso, o ZEST foveal parte do esperado do ponto mais central da grade e o relatório mostra o limiar sem p.

### 8.2 Mapas e índices

- **Limiar analisado:** "<0 dB" do dispositivo é analisado como 0 dB (como no HFA).
- **TD** = sensibilidade HFA-eq − esperado para a idade.
- **GH (general height)** = TD na posição floor(0,15·n) em ordem decrescente (7º melhor de 52 no 24-2).
- **PD** = TD − GH.
- **Probabilidades:** menor nível p ∈ {0,5%, 1%, 2%, 5%} tal que o valor fica abaixo do percentil p
  dos normais naquele ponto.
- **MD** (Heijl et al. 1987): Σ(TDᵢ/sᵢ²) / Σ(1/sᵢ²), com sᵢ = DP normativo do TD. A opção "inverseSd"
  (pesos 1/sᵢ, visualFields) é configurável.
- **PSD** (Heijl et al. 1987): √[ (1/n)Σsᵢ² · 1/(n−1) Σ (TDᵢ − MD)²/sᵢ² ].
- **VFI** (Bengtsson & Heijl 2008, como em visualFields): por ponto 100·(1 − |TD|/esperado) quando
  significativo a p < 5% no mapa de PD (no mapa de TD se MD < −20 dB), senão 100; sensibilidade < 0 → 0;
  média ponderada por magnificação cortical w = 1/(0,08·(exc + 0,8)).
- **GHT** (Åsman & Heijl 1992): 5 setores superiores e seus espelhos inferiores; pontuação por ponto pela
  probabilidade do PD (p<5% = 2, p<2% = 5, p<1% = 10, p<0,5% = 20); diferença superior − inferior por
  setor comparada aos percentis 97 e 99 dos normais. "Fora dos limites normais" se algum setor excede o
  P99; "Limítrofe" se excede o P97; "Sensibilidade geral reduzida" se GH < P0,5; "Anormalmente alta" se
  GH > P99,5. **A composição dos setores é uma reconstrução e precisa ser validada contra a figura
  original [DECISÃO PENDENTE]** (`config/ght.json`, status `TO_BE_VALIDATED`). Em grades com `ghtApplicable: false`
  (10-2) o resultado é "Não se aplica" (`analysis.GhtApplicable`), sem diferenças de setor.
- **Escala de cinza:** 10 faixas sobre o valor HFA-eq: < 4 dB preto … ≥ 36 dB branco (limites a cada
  4 dB) **[APROVADO 2026-10-17]**.

## 9. Simulador de paciente

- Campo verdadeiro (dB HFA-eq) = modelo do normativo + deslocamento global + variação local, com
  defeitos opcionais: arqueado superior/inferior (região de Bjerrum nasal, 8°–24,5°, x ≤ 9°, inclui o
  degrau nasal), hemianopsia (meio campo temporal) e escotoma central (≤ 5°), com profundidade
  configurável (padrão 15 dB).
- Observador: P(visto) = fpr + (1 − fpr − fnr)·(1 − Φ((s − t)/σ)), com σ de Henson et al. 2000
  (OPI `SimHenson`, tipo "C") ou constante (OPI `SimGaussian`). Mancha cega fisiológica elíptica
  (5,5° × 7,5°) em (15°, −1,5°): ali só há falsos positivos. Perda de fixação: o estímulo cai em
  retina que enxerga. Latência ~ N(450, 90) ms. A função de Weibull ainda não foi implementada.

## 10. Verificação (Fase 1)

| Critério (SPEC §11) | Resultado |
|---|---|
| Testes do `/core` | 118 testes passando |
| Cobertura | 99% das linhas, 95% dos ramos |
| 24-2 ZEST, observador normal: erro absoluto médio < 2 dB | ZEST 1,26 dB; ZEST rápido 1,45 dB (30 exames simulados cada) |
| 24-2 ZEST: tempo < 5 min | ZEST rápido 4,8 min (máx. 5,5); ZEST padrão 6,9 min (máx. 7,9) |
| Arqueado detectado com PD p < 1% nos pontos certos | 97–99% dos pontos do defeito; 1% de falsos alarmes fora dele |
| Tamanho angular < 5% de erro | verificado para I–V em 4 posições do campo |
| Equivalência com o OPI 3.1.1 em R | 140 cenários (ZEST, fourTwo, FT): sequências de estímulos idênticas, diferença máxima 1,2·10⁻¹³ |

O erro é medido nos pontos cujo limiar verdadeiro está dentro da faixa mensurável do dispositivo.

## 11. Apresentação no headset (Fase 2)

Implementado em `headset/Assets/Perimetry` (lógica testada fora do Unity em `headset/LogicTests`).
Parâmetros em `config/display.json`, `config/timing.json` e `config/headset.json`.

**Geometria.** O conteúdo é preso à cabeça (filho da câmera). Cada estímulo fica na esfera de raio
`virtualStimulusDistanceM` (1 m) **centrada no olho testado**, na direção `Stimulus.Direction(x, y)` (mesma
convenção da cúpula do HFA, §1); o alvo de fixação fica à frente desse olho. Centrar na cabeça em vez do olho
deslocaria todos os pontos ≈ 1,8° (IPD 63 mm a 1 m); teste em `GeometryTests`. A posição dos olhos vem do
runtime XR (`left/rightEyePosition`) e a origem fica registrada no log da sessão. O disco é um polígono de 64
lados com a **mesma área** do círculo (tamanhos de Goldmann são definidos por área). Tamanho angular
verificado para I–V a partir do olho testado: erro < 0,1% (critério < 5%), também no Unity pelo rig real.

**Monocular.** O shader `Perimetry/MonocularUnlit` usa o índice do olho do estéreo single-pass: o olho não
testado recebe `untestedEyeLuminanceCdm2` = 0 (preto) durante verificações, treino e exame. Mensagens entre os
olhos e telas do operador aparecem nos dois olhos.

**Luminância e dithering.** O painel é de 8 bits; perto do fundo de 10 cd/m² um degrau de código vale
≈ 0,23 cd/m² (≈ 26 dB do dispositivo), então sem dithering os estímulos mais fracos se confundem com o fundo
(erro > 2 dB a partir de 28 dB, estímulo invisível a 30 dB). O shader aplica dithering ordenado Bayer 4×4
(com deslocamento por quadro) no nível codificado em sRGB, saída em espaço linear para um alvo sRGB:
erro da luminância média renderizada ≤ 0,43 dB em 0–30 dB do dispositivo (`DitheredLuminance_TracksDbScale_0To30`).
Exige espaço de cor Linear, HDR e pós-processamento desligados (feito por *Perimetry > Configure Project*).
O alvo de fixação é um losango amarelo de 0,8°, 30 cd/m². Texto em cinza-escuro (2 cd/m²) abaixo do centro.

**Tempo.** A duração de 200 ms é quantizada em quadros inteiros da taxa medida (90 Hz → 18 quadros; 72 Hz →
14 quadros = 194 ms; o Quest inicia a 72 Hz e o pedido de 90 Hz ainda não está ligado). O início do estímulo é o
primeiro quadro após o ISI; a janela de resposta (1000 ms) conta a partir dele. A resposta vale do início do
estímulo até o fim da janela; apertos antes do início são registrados como `earlyPress` e não contam. Resolução
do tempo de resposta: um quadro. A latência inclui o atraso fixo do display (dezenas de ms), igual para todos os
estímulos.

**Pausa.** Segurar o botão por `pauseHoldMs` = 1500 ms durante o exame pausa (comportamento do HFA); tirar o
headset também pausa. A apresentação em curso é descartada (`Disposition = "aborted"`, não entra na estratégia
nem nas contagens de confiabilidade) e o mesmo tipo de apresentação volta depois. A retomada espera
`resumeAfterPauseDelayMs` = 1500 ms. As pausas ficam em `Reliability.Pauses`; `DurationMs − PausedMs` é o tempo
efetivo.

**Painel ao vivo e comandos remotos (Fase 4).** O headset envia ao portal, a cada segundo, um instantâneo do que o
`ExamSession` já contém (estado do fluxo, limiares das localizações concluídas em dB do aparelho e equivalente HFA,
contagens de confiabilidade com o limite de alerta da config, última apresentação); nada é calculado no portal e
nada muda na psicofísica. O médico pode pausar, retomar e encerrar pelo portal: a pausa remota é idêntica ao paciente
segurar o botão (apresentação em curso descartada, pausa em `Reliability.Pauses` com motivo `operator`), a retomada
respeita o mesmo `resumeAfterPauseDelayMs` e só vale com o headset na cabeça, e encerrar equivale à interrupção pelo
operador. O exame nunca depende da rede: sem portal o fluxo é o mesmo, e os arquivos são enviados depois
(`docs/SYNC_API.md`).

**Fluxo do paciente (SPEC §8).** Boas-vindas (verifica o botão) → operador escolhe exame e idade, se não houver
`exam-order.json` → instruções → por olho: apresentação do olho, nitidez do alvo (lembrete de IPD/foco),
verificação de oclusão → treino (só no primeiro olho) → pronto → exame (mancha cega + grade) → descanso entre
olhos → "Exame concluído. Aguarde o médico." Nenhum resultado é mostrado ao paciente (teste de mensagens).
Interrupção pelo operador grava o olho em andamento com `Aborted = true`, e a análise recusa esse registro.

**Fase 5 no headset (padrão proposto, aguardando confirmação do product owner).** Presets em `config/headset.json`
para **30-2, 10-2 e 24-2C** (ZEST e ZEST rápido) e variantes opcionais do 24-2 rápido com fóvea, eye-tracking,
oculocinético e escolha forçada; o primeiro preset (24-2 ZEST, modo A) continua o padrão. O 30-2 chega a 27° (38° na
diagonal), dentro do campo útil configurado (`usableHalfFieldDeg` 50°) e do campo de visão do Quest 3 (≈ 55° horizontal
/ 48° vertical de meio-ângulo), mas perto da borda da lente, onde a nitidez cai: verificar no aparelho.

- **Limiar foveal:** com `fovealThreshold: true` no preset, a tela "pronto" já mostra o **diamante de 4 pontos**
  (`FixationTargetKind.Diamond4`: pontos de `fixationTarget.diamondDotSizeDeg` = 0,4° a `foveal.diamondHalfSizeDeg` =
  1,5° do centro, mesma cor e luminância do alvo) e explica ao paciente que a luz aparece no meio deles
  (`exam.ready.foveal`). As apresentações foveais do `/core` vêm primeiro, com o diamante; ao terminar, o losango central
  volta e o exame segue. Durante o exame não há texto (como sempre).
- **Eye-tracking (Quest Pro):** `GazeTracking` lê o dispositivo *Eye Gaze* do OpenXR (`XR_EXT_eye_gaze_interaction`,
  perfil *Eye Gaze Interaction* ligado por *Perimetry > Configure Project*) ou os `eyesData` do XR legado, leva a direção
  ao espaço da câmera (mesmo espaço do rig preso à cabeça) e a converte em coordenadas de campo
  (`AngularGeometry.FromDirection`). A cada quadro o fluxo passa ao `PresentationTimeline` o erro angular entre o olhar e
  o alvo da apresentação; o resultado leva `GazeTracked`, o erro no quadro do início do estímulo (`GazeErrorDeg`) e o
  máximo durante os quadros do estímulo (`GazeMaxErrorDeg`), e o `/core` aplica a regra do §5. O olhar conta como
  disponível enquanto a última amostra tem menos de `oculokinetic.gazeStaleMs` = 500 ms; sem amostras (Quest 3) nada
  muda e o log registra `gaze unavailable`. A permissão `com.oculus.permission.EYE_TRACKING` entra no manifesto no build
  e só é pedida em tempo de execução quando existe um rastreador (nunca no Quest 3). Um preset `CentralEyeTracking` num
  aparelho sem rastreador roda **sem monitoramento de fixação** e o log avisa.
- **Modo B (oculocinético):** a cada apresentação o alvo já aparece na nova posição (`TargetX/TargetY`) no primeiro
  quadro, com a **animação de travamento** (`fixationTarget.lockAnimation`: 1,6× maior e 60% da luminância, convergindo
  linearmente ao alvo normal). Sem eye-tracking a fase de travamento dura `lockDelayMs` = 600 ms; com eye-tracking termina
  quando o olhar fica ≤ `gazeConfirmDeg` = 3° do alvo por `gazeConfirmMs` = 300 ms, ou após
  `lockDelayMs × gazeConfirmTimeoutFactor` = 3 s sem confirmação (a regra de rejeição no início do estímulo então
  descarta a apresentação se o olho ainda estiver longe). **Depois** do travamento vem o ISI aleatório de sempre
  (600–1200 ms) e então o estímulo, em `AngularGeometry.Compose(alvo, ponto)`; a regra de antecipação e a janela de
  resposta são as do modo A. Os quatro pontos do diamante foveal, quando houver, também são compostos em torno do alvo.
  Instruções próprias (`instructions.oculokinetic`, `exam.ready.oculokinetic`: "siga o losango").
- **Escolha forçada (experimental):** após o ISI, cada intervalo começa com o **piscar do alvo** (o losango some por
  `markerMs` = 100 ms) seguido do intervalo de `intervalMs` = 200 ms (quantizado em quadros; o estímulo só aparece no
  intervalo sorteado pelo `/core`), com `gapMs` = 600 ms entre intervalos; a janela de resposta (1000 ms) conta do fim
  do último intervalo. O aperto é atribuído ao intervalo em curso (do seu piscar até o piscar seguinte; a janela
  pertence ao último intervalo) e o primeiro aperto decide `RespondedInterval`; sem aperto = sem resposta. Apertos antes
  do primeiro piscar são `earlyPress`. Instruções próprias (`instructions.forcedChoice`, `exam.ready.forcedChoice`). A
  simulação quadro a quadro do 24-2 rápido em escolha forçada termina com todos os pontos medidos, sem FP, sem
  Heijl-Krakau e sem regra de antecipação (§6.4).
- **Painel ao vivo:** o instantâneo ganha `gazeAvailable`, `fixation {kind, x, y}`, e em `exam`: `fixationMode`,
  `responseMode`, `fovealThreshold`, `foveal {db, hfaEqDb, censoredLow, presentations}` (quando concluído),
  `reliability.gazeTracked/gazeRejectedPresentations/gazeMeanErrorDeg` e `last.targetX/targetY` (`docs/SYNC_API.md`).

| Item | Default | Status |
|---|---|---|
| Pausa ao segurar o botão | 1500 ms; apresentação em curso descartada | **[DECISÃO PENDENTE]** confirmar |
| Treino | 5 estímulos: 0 dB (9,9), 0 dB (−9,−9), branco (sem luz), 6 dB, 10 dB; feedback 2 s; repete 1 vez se > 2 erros | **[DECISÃO PENDENTE]** |
| Verificação de oclusão | opcional, desligada por padrão (`checks.occlusionCheckEnabled`; o headset já bloqueia bem a luz externa). Quando ligada: antes de cada olho, o paciente fecha o olho testado por 5 s; um disco de 10° no máximo aparece só nesse olho; apertar = falha, chama o médico. Durante o exame os dois olhos ficam abertos | aprovado em 2026-10-18 (desligada por padrão após o primeiro teste no Quest 3) |
| Alvo de fixação | losango amarelo 0,8°, 30 cd/m² | **[DECISÃO PENDENTE]** |
| Duração a 72 Hz | 194 ms (14 quadros) até o pedido de 90 Hz ser ligado | **[DECISÃO PENDENTE]** |
| Idade sem pedido de exame | 50 anos, ajustada pelo operador (afeta o prior do ZEST e a análise) | default |
| Diamante foveal | 4 pontos de 0,4° a 1,5° do centro, amarelos, 30 cd/m² | Fase 5, **padrão proposto** |
| Travamento no modo B | 600 ms (ou olhar ≤ 3° por 300 ms; timeout 3 s), animação 1,6× / 60%; ISI depois do travamento | Fase 5, **padrão proposto** |
| Marcador da escolha forçada | losango some por 100 ms antes de cada intervalo | Fase 5, **padrão proposto** |
| Demonstração para o paciente antes de colocar o headset | página/animação em pt-BR no portal (monitor ou tablet) | **Fase 6** (decisão de 2026-10-18: depois da Fase 5) |
| Olhar "disponível" | última amostra há < 500 ms | Fase 5, default técnico |

## 12. Portal e relatório (Fase 3)

- **Análise:** o portal não recalcula nada. Na importação, cada exame passa por `perimetry report` (mesmo `/core`
  e mesma `config/` do headset). Se o hash da config do exame difere do da análise, o relatório mostra aviso.
  A análise e o arquivo bruto ficam guardados; o arquivo bruto permite reanálise e reverificação do HMAC.
- **Layout Single Field Analysis (HFA):** cabeçalho (paciente, programa, olho, data, idade), confiabilidade
  (perdas de fixação, FP, FN com "xx" quando acima do limite, duração efetiva e pausas, mancha cega), parâmetros
  (estímulo, fundo, estratégia, correção, calibração, escala), avisos em vermelho (protótipo/ANVISA, NÃO
  CALIBRADO, normativo PROVISÓRIO, confiabilidade), mapas numérico de limiares, escala de cinza, desvio total e
  desvio padrão (pattern) numéricos e de probabilidade, GHT, VFI, MD e PSD com p. Campo visto pelo paciente
  (OS espelhado), x à direita, y para cima. **VFI com uma casa decimal** (99,5%, não 100%), por decisão do
  product owner em 2026-10-18; o HFA imprime inteiro.
- **Limiares:** em dB equivalente HFA (dB do aparelho + `HfaOffsetDb`), a mesma escala do normativo e da escala de
  cinza. Abaixo do mínimo mensurável do aparelho o valor sai como "<NN". *(Escolha de apresentação; dB do
  aparelho continua no CSV e no JSON.)*
- **Escala de cinza:** 10 faixas de `GrayBand` do `/core` (0 = preta … 9 = branca), dither ordenado Bayer 4×4 numa
  grade global de 0,3°, células de 6° centradas nos pontos.
- **Probabilidades:** símbolos p < 5%, 2%, 1% (padrões de densidade crescente) e p < 0,5% (quadrado preto);
  ponto sem significância = ponto pequeno; mancha cega omitida.
- **Rastreio:** mapa ○ visto, r visto no reteste, ■ não visto.
- **Tendência:** regressão linear simples de MD e VFI contra o tempo (inclinação/ano, EP, p bicaudal pela t com
  n − 2 gl), sem exames interrompidos nem rastreios. Descritiva; análise por eventos (GPA) fica para depois.
- **PDF:** A4, uma página por olho, impresso pelo Chromium a partir da mesma página da tela.
- **Fase 5 (campos novos no `perimetry report`, schema `perimetry-report/1` mantido, chaves adicionais):**
  `exam.FovealThresholdHfaDb` e `exam.Foveal` (limiar foveal; `null` quando não testado) → linha "Limiar foveal";
  `exam.Reliability.GazeTracked/GazeMeanErrorDeg/GazeSamples/GazeMaxErrorDeg/GazeTrace` → barra de gaze trace no
  rodapé (erro por apresentação, limite de 3° marcado, rejeitadas em destaque), só quando houve rastreamento;
  `exam.GhtApplicable` / `analysis.GhtApplicable` → "GHT: não se aplica" no 10-2; `exam.Settings.FixationMode`
  (`Oculokinetic`) e `exam.Settings.ResponseMode` (`ForcedChoice`) nos parâmetros, com os avisos correspondentes em
  `warnings`. Grades 10-2, 30-2 e 24-2C usam a mesma análise; o leiaute dos mapas segue as coordenadas de cada grade.
- **Laudo do portal e PNG (Fase 5, padrão proposto, aguardando confirmação do product owner):**
  - *Limiar foveal* nos parâmetros, como o "Fovea: 35 dB / OFF" do HFA: `NN dB` (equiv. HFA) com a marca de
    probabilidade do desvio total foveal (`p < 5%` …), `<NN dB` quando censurado no piso do aparelho, ou "não testado".
  - *Monitor de fixação*: "Heijl-Krakau (mancha cega)", "Eye-tracking", "Oculocinética (modo B)" ou "Não monitorada";
    na escolha forçada acrescenta "Escolha forçada (experimental)". *Alvo de fixação*: "Central", "Móvel (oculocinético)"
    e "+ losango de 4 pontos na fóvea" quando houve teste foveal. Com eye-tracking, a linha "Olhar descartado: N de M"
    (apresentações rejeitadas / rastreadas) entra no bloco de confiabilidade.
  - *Barra de rastreamento do olhar* (rodapé, só com `GazeTracked`): um traço fino por apresentação, na ordem do exame;
    altura = erro do olhar no início do estímulo, cortado em 10° (escala 0 / 5 / 10°), linha tracejada no limite de
    rejeição (`gaze.maxErrorDeg`, 3°); apresentação descartada = traço para baixo, como as marcas "down" do HFA.
    Legenda "Rastreamento do olhar: erro médio N,N°, N descartadas (M apresentações rastreadas)". Geometria SVG pura.
  - *GHT*: "Não se aplica" (sem diferenças de setor) quando `GhtApplicable` é falso (10-2); a lista do paciente mostra o
    mesmo texto. O importador guarda `fovealHfaDb` e `gazeTracked` no exame para as listas ("· fóvea NN dB", "· eye-tracking").
  - *Mapas por grade*: passo principal da malha = maior intervalo entre coordenadas que ocorre ao menos 3 vezes (6° no
    24-2/30-2/24-2C, 2° no 10-2); eixos com marcas a cada 10° (5° no 10-2); escala de cinza com células do passo
    principal e, no 24-2C, os 10 pontos maculares extras desenhados por cima em células de 2°, com números e símbolos
    menores. O marcador da mancha cega só aparece em grades que a alcançam.
  - *Pedido de exame*: grade, monitor de fixação, limiar foveal e modo de resposta opcionais, escritos no
    `exam-order.json` só quando diferem do preset (contrato em `docs/SYNC_API.md`).

## 12a. Sincronização e painel ao vivo (Fase 4)

- O exame no headset continua **independente da rede**: gravação local primeiro (HMAC), envio depois; sem rede o
  fluxo é o da Fase 2. Contrato completo em `docs/SYNC_API.md`.
- **Painel ao vivo** (equivalente à tela do operador do HFA): estado do fluxo, mapa de limiares preenchendo com os
  pontos já terminados (dB equivalente HFA calculado no headset pelo `/core`; "<NN" abaixo do mínimo mensurável),
  pontos em teste, último estímulo (posição, intensidade, visto/não visto), perdas de fixação, FP e FN com "xx"
  acima do limite do `config/`, mancha cega, duração. Nenhum número é calculado no portal.
- **Pausar / Retomar / Encerrar** pelo portal: chegam ao headset no envio seguinte (≤ 1 s). A pausa remota é
  idêntica à pausa pelo botão do paciente (apresentação em curso descartada, intervalo registrado em
  `Reliability.Pauses` com motivo `operator`); encerrar salva o olho em andamento com `Aborted: true`. Nenhum
  resultado é mostrado ao paciente.
- Pedido de exame enviado ao headset: só o código pseudônimo, idade, correção, exame e semente; o operador confirma
  na tela de preparação. O exame importado pela rede traz a integridade **verificada** pela chave enviada no pareamento.

## 13. Referências

1. Heijl A, Lindgren G, Olsson J. A package for the statistical analysis of visual fields. *Doc Ophthalmol Proc Ser* 1987;49:153–168. (MD, PSD)
2. Åsman P, Heijl A. Glaucoma Hemifield Test: automated visual field evaluation. *Arch Ophthalmol* 1992;110:812–819.
3. Bengtsson B, Heijl A. A visual field index for calculation of glaucoma rate of progression. *Am J Ophthalmol* 2008;145:343–353.
4. Heijl A, Krakau CET. An automatic static perimeter, design and pilot study. *Acta Ophthalmol* 1975;53:293–310. (monitoramento pela mancha cega)
5. King-Smith PE, Grigsby SS, Vingrys AJ, Benes SC, Supowit A. Efficient and unbiased modifications of the QUEST threshold method. *Vision Res* 1994;34:885–912. (ZEST)
6. Turpin A, McKendrick AM, Johnson CA, Vingrys AJ. Properties of perimetric threshold estimates from Full Threshold, ZEST, and SITA-like strategies, as determined by computer simulation. *Invest Ophthalmol Vis Sci* 2003;44:4787–4795.
7. Henson DB, Chaudry S, Artes PH, Faragher EB, Ansons A. Response variability in the visual field. *Invest Ophthalmol Vis Sci* 2000;41:417–421.
8. Turpin A, Artes PH, McKendrick AM. The Open Perimetry Interface: an enabling tool for clinical visual psychophysics. *J Vis* 2012;12(11):22.
9. Marín-Franch I, Swanson WH. The visualFields package: a tool for analysis and visualization of visual fields. *J Vis* 2013;13(4):10.
10. Bengtsson B, Olsson J, Heijl A, Rootzén H. A new generation of algorithms for computerized threshold perimetry, SITA. *Acta Ophthalmol Scand* 1997;75:368–375.
11. Iowa Head-Mounted Perimeter (Wall et al.), "Perimetry of the Central Visual Field Using a Head-Mounted Open-Source Perimeter", *Vision* 2026, e estudo de validação vs. Octopus 900 (IOVS), conforme citados na SPEC §10. *Referência bibliográfica completa a confirmar.*
12. "Evaluating the Clinical Validity of Commercially Available VR Headsets for Visual Field Testing", *Vision* 2025, conforme citado na SPEC §10 (limiar mínimo ≈ 20 dB HFA em Quest 2 / Pico Neo 3). *Referência bibliográfica completa a confirmar.*
