# Prompt para Claude Code — Perimetria Computadorizada em Meta Quest

> Fonte de verdade do projeto (especificação do product owner clínico). Copiada sem alterações
> da mensagem original de 2026-10-17. `CLAUDE.md` referencia este arquivo.

---

## PROMPT

Você vai construir, do zero, um sistema de **perimetria computadorizada estática automatizada (SAP)** para o headset **Meta Quest 3 / Quest 3S** (com suporte opcional a Quest Pro para eye-tracking nativo), inspirado em produtos como o Vivid Vision Perimetry (VVP) e com saída clínica no formato que o oftalmologista já conhece: o **Humphrey Field Analyzer (HFA)**, padrão-ouro atual para campo visual computadorizado.

Eu sou **médico oftalmologista**. Vou supervisionar cada exame, validar o comportamento clínico e, posteriormente, registrar o software. Trate-me como *product owner clínico*: sempre que uma decisão tiver impacto clínico (escala de dB, estratégia de limiar, critérios de confiabilidade, apresentação dos mapas), implemente o padrão HFA por default, deixe o parâmetro configurável e **me pergunte** antes de inventar algo diferente. Não "simplifique" a psicofísica silenciosamente.

### 0. Princípios de trabalho

1. Trabalhe em **fases** (listadas na seção 9). Ao final de cada fase, rode build/testes, me entregue um resumo curto do que foi feito e do que ficou pendente, e aguarde meu OK antes da próxima.
2. Crie um `CLAUDE.md` na raiz com: stack, comandos de build/teste, convenções, e um resumo dos parâmetros clínicos. Mantenha-o atualizado.
3. Crie um `docs/CLINICAL_SPEC.md` descrevendo toda a psicofísica implementada (estímulo, fundo, escala de dB, algoritmos, índices) com referências. Esse documento será a base da futura documentação regulatória.
4. Tudo que for **constante clínica** (luminâncias, tamanhos, durações, grades de pontos, limiares de confiabilidade) fica em arquivos de configuração versionados (`config/*.json` ou ScriptableObjects), nunca hardcoded espalhado no código.
5. Código em inglês; UI do paciente e do médico em **português do Brasil** (pt-BR), com i18n preparado para en-US.
6. Determinismo e rastreabilidade: cada exame grava a sequência completa de apresentações (localização, intensidade, tempo, resposta, latência, estado da fixação) para auditoria e reprocessamento offline.
7. Nunca afirme na UI, no README ou nos relatórios que o sistema é um dispositivo médico aprovado. Rotule como "Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA".

### 1. Stack técnica

- **Unity 6 LTS** (ou 2022.3 LTS se o Meta XR SDK exigir), **URP**, **OpenXR** com **Meta XR Core SDK** + **Meta XR Interaction SDK** (Unity Package Manager / Asset Store). Build target: Android (Quest). Use o **Meta XR Simulator** para rodar no desktop durante o desenvolvimento.
- C# com testes unitários em **Unity Test Framework** (EditMode para toda a lógica psicofísica, que deve ser pura C# sem dependência de MonoBehaviour).
- Persistência local no headset em **SQLite** (ou JSON assinado) + exportação.
- **Backend/Portal do médico** (fase posterior): **Next.js + TypeScript + PostgreSQL** (ou Supabase) com autenticação, lista de pacientes, histórico de exames, visualização dos mapas e geração de **PDF** no layout "single field analysis" do HFA. Comunicação headset ↔ backend via REST/HTTPS com token. Deve funcionar **offline-first**: o exame nunca depende de rede; sincroniza depois.
- Separe o repositório em `/headset` (Unity), `/core` (biblioteca C# pura de psicofísica, compartilhada e testável), `/portal` (web), `/docs`.

### 2. Hardware e restrições ópticas (explicite isso no código e nos docs)

- **Campo de visão útil do Quest 3**: ~110° horizontal / ~96° vertical total, portanto ~±50° a partir do centro. Os padrões 24-2 (até 27° nasal) e 30-2 (até 30°) cabem; 60-4 **não cabe** — não implemente.
- **Luminância**: o display do Quest atinge no máximo ~100 cd/m² (≈ 314 asb), muito abaixo dos 10.000 asb do estímulo máximo (0 dB) do HFA. Consequências que devem ser tratadas explicitamente:
  - O fundo-padrão do HFA é **31,5 asb (10 cd/m²)**. Implemente o fundo em 10 cd/m² e permita calibrar.
  - A escala de dB será **relativa ao máximo do dispositivo**, calibrada por fotometria. Implemente `LuminanceCalibration`: tabela gamma (valor RGB → cd/m²) por olho, carregável de um arquivo de calibração gerado com fotômetro/colorímetro. Até existir calibração real, use uma curva gamma sRGB padrão **e marque todo relatório como "não calibrado"**.
  - Documente a fórmula: `dB = 10 * log10(L_max / L_estímulo)` onde `L_max` é a luminância máxima calibrada do dispositivo. Exponha um mapeamento opcional "dB equivalente HFA" apenas para fins de comparação, deixando claro que é uma transformação.
  - Use renderização em **HDR/linear color space** e evite banding: trabalhe em float e use dithering ao converter para 8/10 bits, pois os passos de 1–2 dB perto do limiar exigem resolução de luminância fina.
- **Estímulo**: **Goldmann tamanho III** (diâmetro 0,43° = 4 mm² a 30 cm), configurável para I–V; duração **200 ms**; cor branca sobre fundo branco (perimetria acromática padrão). Renderize o estímulo como disco em uma esfera/casca a distância fixa virtual (ex.: 1 m), **em coordenadas angulares** relativas ao centro de fixação — nunca em pixels de tela. Compense a distorção da lente (o SDK já faz) e verifique o tamanho angular com um teste automático.
- **Teste monocular**: o olho não testado recebe tela preta. Tenha um fluxo claro OD → OS (ou ordem configurável) com pausa entre olhos.
- **Resposta do paciente**: botão do controle Touch (qualquer gatilho) **ou** botão de um clicker Bluetooth; detecção de pressão com timestamp de alta resolução. Janela de resposta ≈ 1.000 ms após o início do estímulo; intervalo entre estímulos aleatório (ex.: 600–1.200 ms) para evitar antecipação.
- **IPD e ajuste**: antes do exame, tela de verificação de nitidez do alvo de fixação e lembrete para ajustar IPD/foco. Permitir anotar a correção refrativa usada (lente de prova no headset ou óculos).

### 3. Fixação — duas estratégias, ambas implementadas

**A) Fixação central clássica (modo HFA)**: alvo central fixo (ponto/cruz amarela ou diamante), com monitoramento por:
- **Método de Heijl-Krakau**: localize a mancha cega no início (varredura ao redor de 15° temporal, 1,5° abaixo do meridiano horizontal) e, periodicamente, apresente estímulos supraliminares na mancha cega. Resposta = perda de fixação. Reporte **Perdas de fixação (x/y)**.
- **Eye-tracking nativo** quando disponível (Quest Pro via OVREyeGaze / OpenXR `XR_EXT_eye_gaze_interaction`): descarte ou repita apresentações quando o olhar estiver > N graus do alvo; grave o traçado de olhar (gaze trace) para exibir no relatório, como o HFA faz.

**B) Fixação oculocinética (modo "VVP-like")**: antes de cada estímulo, o alvo de fixação aparece em uma nova posição e o paciente deve olhar para ele; o estímulo só é apresentado após confirmação (eye-tracking, ou após um tempo fixo + animação de "lock" quando não há eye-tracking). As posições de teste são calculadas **relativas à posição atual do alvo**, de modo que o conjunto de pontos testados no espaço retiniano seja exatamente o da grade escolhida (24-2 etc.). Esse modo reduz fadiga. Documente a geometria com clareza.

O médico escolhe o modo ao configurar o exame. Default: **A** (comparabilidade com HFA).

### 4. Padrões de teste (grades de pontos)

Implemente como dados (JSON) com coordenadas em graus (x temporal/nasal, y superior/inferior), com espelhamento automático para OD/OS:
- **24-2** (54 pontos: grade de 6° deslocada 3° do meridiano, até 24°, mais 2 pontos nasais a 27°).
- **30-2** (76 pontos).
- **10-2** (68 pontos, grade de 2° até 10°).
- **24-2C** (24-2 + 10 pontos maculares adicionais).
- **Rastreio (screening)** supraliminar em qualquer grade.
- **Grade customizada** via CSV (x, y em graus).
- Teste de **limiar foveal** opcional no início.
Inclua testes unitários que verifiquem contagem de pontos, simetria, e que a mancha cega (15°, −1,5°) esteja onde o HFA espera no 24-2/30-2.

### 5. Estratégias de limiar (biblioteca `/core`, C# puro, 100% testável)

1. **Full Threshold 4-2**: escada com passos de 4 dB até a primeira reversão, depois 2 dB até a segunda; limiar = último valor visto. Valores iniciais a partir de 4 pontos-âncora (um por quadrante, ~9°,9°) e propagação para vizinhos.
2. **Algoritmo bayesiano tipo ZEST / SITA-like** (não copie o SITA, que é proprietário; implemente ZEST com prior baseado em distribuição normal × glaucomatosa, critério de parada por desvio-padrão da posterior, e atualização dos priors dos vizinhos). Documente a diferença. Este é o modo "Rápido".
3. **Screening supraliminar**: estímulo a X dB acima do limiar esperado pela idade (ex.: 6 dB), registra visto / não visto, re-testa não vistos.
4. **Estratégia tolerante a "chutes"** (inspirada no VVP): paciente responde em **qual de N intervalos temporais** o estímulo apareceu (escolha forçada) em vez de "viu/não viu". Implemente como opção experimental.

Todas as estratégias devem funcionar contra um **simulador de paciente** (`/core/Simulation`): modelo de observador com função psicométrica (Gaussiana/Weibull, slope configurável), taxas de falso-positivo/negativo, campo de entrada sintético (normal, defeito arqueado, hemianopsia, escotoma central). Use isso para testes automáticos de acurácia e tempo de exame (ex.: erro médio < 2 dB em campo normal, duração 24-2 ZEST < 5 min simulados).

### 6. Índices de confiabilidade (iguais ao HFA, mesmo nome, mesma apresentação)

- **Perdas de fixação** (Heijl-Krakau e/ou eye-tracking): alerta se > 20%.
- **Falsos positivos**: apresentações "vazias" (sem estímulo) ou janelas sem estímulo em que o paciente responde; alerta se > 15% (ou > 33% no modo full threshold conforme preferir — configurável).
- **Falsos negativos**: re-apresentação supraliminar (+9 dB) em pontos já com limiar; não resposta = FN; alerta se > 33%.
- **Flutuação de curto prazo** opcional (re-teste de 10 pontos).
- Duração do exame, número de apresentações, modo de fixação, calibração usada.

### 7. Análise e relatório (layout HFA "Single Field Analysis")

Gere, por olho:
- **Mapa numérico de limiares** (dB) e **escala de cinza (grayscale)** com a convenção de tons do HFA (0 dB preto ... ≥ 36 dB branco, 10 faixas).
- **Desvio total (Total Deviation)** e **Desvio padrão (Pattern Deviation)** — numéricos e mapas de probabilidade (símbolos p < 5%, 2%, 1%, 0,5%).
- **Índices globais**: **MD (mean deviation)**, **PSD (pattern standard deviation)**, **VFI (visual field index)** — implemente conforme literatura aberta (Heijl, Bengtsson).
- **GHT (Glaucoma Hemifield Test)**: implemente uma versão baseada em comparação dos 5 setores superiores vs. inferiores conforme Åsman & Heijl 1992, com saídas "Dentro dos limites normais / Limítrofe / Fora dos limites normais / Sensibilidade geral reduzida / Anormalmente alta".
- **Banco de dados normativo**: o do HFA é proprietário e não existe para este hardware. Estruture o código para que `NormativeDatabase` seja **um plug-in de dados** (idade × localização → média e desvio-padrão). Entregue: (a) um normativo **provisório sintético** baseado na literatura (declínio ~0,6–1 dB/década, gradiente de colina de visão) claramente marcado como "PROVISÓRIO"; (b) ferramenta no portal para que eu **colete e construa o normativo próprio** com voluntários saudáveis (coorte, exclusões, estatística por faixa etária, export). Enquanto o normativo for provisório, todos os mapas de probabilidade/índices devem exibir aviso em vermelho.
- **Gaze trace** (se eye-tracking) e barra de fixação no rodapé, como no HFA.
- Comparação entre exames: **overview** (série de grayscales), e base para **análise de progressão** (regressão linear de MD e VFI vs. tempo; GPA-like deixar como fase futura).
- Exportar: **PDF A4** no layout HFA, **CSV/JSON** com dados brutos, PNG dos mapas. Prepare o modelo de dados para futura exportação DICOM (OPV — Ophthalmic Visual Field SOP Class) sem implementar agora.

### 8. Fluxo de uso

**No headset (paciente, guiado por voz e texto em pt-BR):**
1. Tela de boas-vindas → seleção do exame pré-configurado pelo médico (ou QR code/código curto) → instruções com demonstração interativa (3–5 estímulos de treino com feedback).
2. Verificação: nitidez do alvo, oclusão do olho correto, resposta do botão.
3. Localização da mancha cega (modo A).
4. Exame olho 1 → pausa → exame olho 2. Pausa pode ser acionada pelo paciente segurando o botão.
5. Tela final neutra ("Exame concluído. Aguarde o médico."). **Nunca** mostrar resultado ao paciente.

**No portal do médico:**
- Cadastro de paciente (nome, data de nascimento, ID interno, olho, refração, diagnóstico). Dados sensíveis conforme **LGPD**: criptografia em repouso, log de acesso, consentimento, pseudonimização para exportação de pesquisa.
- Criar exame: padrão, estratégia, modo de fixação, tamanho do estímulo, olhos.
- Painel "ao vivo" (via LAN/WebSocket quando disponível): mapa preenchendo em tempo real, índices de confiabilidade, botão de pausar/abortar — equivalente à tela do operador do HFA.
- Histórico, relatório PDF, comparação, export.
- Página de **calibração do dispositivo**: upload da tabela fotométrica, data, validade, dispositivo (serial).

### 9. Fases de entrega (pare ao final de cada uma)

- **Fase 1 — `/core`**: grades, escala de dB, estratégias 4-2 e ZEST, screening, confiabilidade, simulador de paciente, índices MD/PSD/VFI/TD/PD/GHT com normativo provisório, testes unitários. Sem Unity. Relatório em console/PNG via script.
- **Fase 2 — Headset MVP**: projeto Unity, render do estímulo em coordenadas angulares, teste de tamanho angular, fixação central + Heijl-Krakau, 24-2 ZEST monocular, resposta por controle, gravação local JSON, fluxo paciente completo. Rodando no Meta XR Simulator e em APK.
- **Fase 3 — Portal**: Next.js + Postgres, pacientes, exames, importação dos JSON do headset, visualização dos mapas, PDF layout HFA, calibração.
- **Fase 4 — Sincronização e ao vivo**: REST + WebSocket, offline-first, painel ao vivo, auth.
- **Fase 5 — Fixação oculocinética, eye-tracking (Quest Pro), 10-2/30-2/24-2C, teste foveal, escolha forçada.**
- **Fase 6 — Ferramenta de coleta de normativo, progressão, hardening de segurança/LGPD, documentação regulatória inicial (SRS, gestão de risco ISO 14971 simplificada, rastreabilidade requisito→teste).**

### 10. Arte prévia e código aberto a consultar (NÃO reinvente o que já existe)

Antes de implementar a Fase 1, estude e porte/adapte, respeitando licenças (Apache 2.0 permite uso comercial; GPL exigiria abrir nosso código — verifique a licença exata de cada repositório antes de copiar qualquer linha e registre no `docs/THIRD_PARTY.md`):

- **OPI — Open Perimetry Interface** (R, Apache ≥ 2.0): `https://github.com/turpinandrew/OPI` e espelho CRAN `https://github.com/cran/OPI`. Contém implementações de referência de **ZEST, 4-2 staircase, Full Threshold, MOCS, QUEST+, KTPsi**, conversão **dB ↔ cd/m²** (`dbTocd`, `cdTodb`), simuladores de observador (`SimHenson`, `SimGaussian`, `SimYes/SimNo`) e drivers para headsets (`PicoVR`, `PhoneHMD`, `Display`). Porte a lógica psicofísica para C# na `/core` mantendo os mesmos nomes de parâmetros, e valide a sua implementação **comparando saídas com o OPI em R** nos mesmos cenários simulados (crie um script `tools/opi-crosscheck/` que roda R e compara). Use também a arquitetura cliente-servidor do OPI (algoritmo em um processo, apresentação de estímulos em outro) como inspiração para o painel ao vivo.
- **visualFields** (R): `https://github.com/imarinfr/vf1`. Referência para **Total Deviation, Pattern Deviation, mapas de probabilidade, MD/PSD, normativos e grayscale**. Reproduza as fórmulas e estrutura de dados do normativo; compare resultados numéricos.
- **PyVF** (Python): `https://github.com/constructor-s/PyVF` — framework de simulação de estratégias; útil como segunda referência para o simulador de paciente.
- **Iowa Head-Mounted Perimeter (Iowa-HMP)**: perímetro open-source em smartphone + headset usando OPI (artigos: Wall et al. / "Perimetry of the Central Visual Field Using a Head-Mounted Open-Source Perimeter", Vision 2026; e o estudo de validação vs. Octopus 900 no IOVS). Leia para entender as limitações de faixa dinâmica já documentadas.
- **Literatura sobre limitação de luminância em VR**: estudos mostram que Quest 2 / Pico Neo 3 só atingem limiar mínimo ≈ 20 dB na escala HFA. Isso confirma a necessidade da escala relativa ao dispositivo e da calibração fotométrica descritas na seção 2. Cite no `CLINICAL_SPEC.md`.
- **Produtos comerciais para comparação funcional** (apenas como referência de UX/relatório; não copie assets): Vivid Vision Perimetry (fixação oculocinética, padrões 24-2/30-2/10-2/Ladybug, custom CSV), Olleyes VisuALL (Unity + cloud + webapp, FDA Class I), Heru, Virtual Field VF3, M&S SmartSystem VR, Oculera (Pico). A revisão sistemática "Evaluating the Clinical Validity of Commercially Available VR Headsets for Visual Field Testing" (Vision, 2025) resume o desempenho desses dispositivos vs. HFA SITA 24-2.

Não existe, até onde pesquisei, um projeto open-source maduro em **Unity para Meta Quest** com relatório estilo HFA — por isso este projeto parte do zero no headset, mas **reaproveita a psicofísica do OPI/visualFields**.

### 11. Critérios de aceite mínimos

- Todos os testes de `/core` passam; cobertura > 80% na biblioteca psicofísica.
- Em simulação com observador normal, 24-2 ZEST: erro absoluto médio < 2 dB, tempo < 5 min; defeito arqueado sintético detectado com PD p < 1% nos pontos corretos.
- Tamanho angular do estímulo verificado automaticamente com erro < 5%.
- APK instala e roda no Quest 3 sem rede.
- PDF gerado é visualmente reconhecível por um oftalmologista como análogo ao Single Field Analysis do HFA.

Comece pela **Fase 1**. Antes de escrever código, me apresente em até 20 linhas as decisões de arquitetura que você vai tomar e qualquer dúvida clínica que precise que eu responda.
