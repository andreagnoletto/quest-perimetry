# Gestão de risco simplificada (ISO 14971) — Perimetria Computadorizada em Meta Quest

> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.**
> Este documento não afirma nem implica aprovação regulatória de nenhuma autoridade. É a primeira
> versão (Fase 6) de uma análise de risco no espírito da ISO 14971, restrita ao software e ao seu uso
> pretendido em pesquisa; não substitui um arquivo de gestão de risco completo nem a análise de
> usabilidade (IEC 62366-1).

Versão 0.1 · 2026-10-18 · Fase 6 · Revisão pendente do product owner clínico (ele é a autoridade para
a severidade de cada dano e para a aceitação do risco residual).

## 1. Escopo, uso pretendido e premissas

- Uso pretendido: ver `SRS.md` §1 (rascunho). O sistema **não emite diagnóstico**; o laudo é
  interpretado por oftalmologista.
- Classificação de segurança de software assumida: **IEC 62304 classe B** (um erro pode contribuir para
  lesão não grave por decisão clínica errada; nenhuma função controla energia aplicada ao paciente além
  da luz do display de consumo). A confirmar pelo product owner.
- Danos considerados: decisão clínica inadequada por resultado errado ou mal interpretado (atraso de
  diagnóstico, tratamento desnecessário), exposição de dados pessoais de saúde (LGPD), desconforto
  ou mal-estar do paciente (fotossensibilidade, náusea em RV), perda de dados de exame.

## 2. Escalas

| Severidade (S) | Descrição |
|---|---|
| 1 | Sem efeito clínico ou de privacidade; incômodo operacional |
| 2 | Exame repetido; desconforto leve e passageiro |
| 3 | Resultado inexato detectável pelo médico; exposição de dado pseudonimizado |
| 4 | Resultado plausível mas errado, podendo alterar conduta; exposição de dado identificável de um paciente |
| 5 | Erro sistemático em muitos exames ou vazamento em massa de dados identificáveis |

| Probabilidade (P) | Descrição (por exame ou por instalação) |
|---|---|
| 1 | Improvável (< 1 em 10 000) |
| 2 | Remota (1 em 1 000) |
| 3 | Ocasional (1 em 100) |
| 4 | Provável (1 em 10) |
| 5 | Frequente (> 1 em 10) |

Risco R = S × P. Aceitável ≤ 6; tolerável de 7 a 12 com controle e justificativa; inaceitável > 12.
Os valores "antes" supõem a ausência do controle listado; "residual" supõe os controles implementados
e verificados. Controles marcados *(planejado)* ainda não existem e o risco residual indicado é o
esperado após a Fase 6.

## 3. Tabela de perigos

| ID | Situação perigosa | Dano | S | P | R | Controles (REQ / código / config) | S | P | Residual | Verificação |
|---|---|---|---|---|---|---|---|---|---|---|
| HAZ-001 | Luminância do estímulo diferente da pedida (display sem calibração fotométrica, curva sRGB nominal, envelhecimento do painel) | Limiares deslocados de forma sistemática; interpretação errada | 4 | 4 | 16 | REQ-STIM-001..004, REQ-STIM-008: escala em dB do dispositivo com offset HFA documentado; tabela de calibração por olho com validade; rótulo **NÃO CALIBRADO** em todo exame e laudo enquanto não houver calibração (`LuminanceCalibration`, `display.json`); dithering verificado ≤ 0,5 dB | 4 | 2 | 8 (tolerável: rótulo torna o estado visível; calibração fotométrica é pré-requisito para uso clínico) | testes de REQ-STIM-001..004, REQ-STIM-008 em `TRACEABILITY.md` |
| HAZ-002 | Olho errado testado ou olho não testado recebendo luz (oclusão falha, troca OD/OS) | Campo atribuído ao olho errado; exame repetido ou conduta errada | 4 | 3 | 12 | REQ-STIM-009: tela preta no olho não testado; REQ-HS-003: verificação de oclusão opcional (desligada por padrão por decisão de 2026-10-18); olho e ordem gravados em cada registro (REQ-REC-005); laudo exibe o olho em destaque | 4 | 1 | 4 | testes de REQ-STIM-009, REQ-HS-003, REQ-REC-005 em `TRACEABILITY.md` |
| HAZ-003 | Erro de geometria angular (estímulo em pixel, esfera centrada na cabeça e não no olho, IPD, distância virtual) | Pontos testados fora da posição retiniana nominal; defeitos deslocados | 4 | 3 | 12 | REQ-STIM-006: colocação em esfera centrada no olho testado, erro angular < 5 %; REQ-STIM-011: grades dentro do campo útil; modo B compõe o estímulo em relação ao alvo (`AngularGeometry.Compose`) | 4 | 1 | 4 | testes de REQ-STIM-006, REQ-STIM-011, REQ-REL-008 em `TRACEABILITY.md` |
| HAZ-004 | Perda de fixação não detectada (mancha cega não localizada, Heijl-Krakau desligado, olhar fora do alvo) | Limiares medidos em posições erradas; exame falsamente confiável | 4 | 3 | 12 | REQ-REL-001..003: Heijl-Krakau com remapeamento e aviso explícito quando a mancha cega não é localizada; REQ-REL-007 (proposto): com eye-tracking, descarte e reteste acima de 3°; REQ-REL-011: contagem e alerta no laudo | 4 | 2 | 8 (tolerável: no Quest 3 o monitoramento é indireto; eye-tracking exige Quest Pro) | testes de REQ-REL-001..003, REQ-REL-007, REQ-REL-011 em `TRACEABILITY.md` |
| HAZ-005 | Índices de confiabilidade errados (contagem de FP/FN incorreta, resposta antecipada contada) | Exame não confiável aceito como válido | 4 | 2 | 8 | REQ-REL-004..006: tentativas de FP/FN com frequências configuradas, descarte de respostas < 100 ms, alerta acima dos limites; log de toda apresentação (REQ-REC-001) permite recontagem offline | 4 | 1 | 4 | testes de REQ-REL-004..006, REQ-REC-001 em `TRACEABILITY.md` |
| HAZ-006 | Exame associado ao paciente errado (código pseudônimo digitado errado, pedido trocado, idade errada) | Laudo no prontuário errado; normativo de idade errada | 4 | 3 | 12 | REQ-SYNC-007/REQ-HS-008: pedido com código, idade e opções enviado pelo portal (sem digitação); REQ-PORT-002: vínculo pelo código e deduplicação; o médico confirma o vínculo na importação manual; idade gravada no exame e mostrada no laudo | 4 | 1 | 4 | testes de REQ-SYNC-007, REQ-HS-008, REQ-PORT-002 em `TRACEABILITY.md` |
| HAZ-007 | Vazamento de dado identificável (arquivos do headset, exportações, logs, instantâneo ao vivo) | Violação da LGPD; dano ao paciente | 5 | 3 | 15 | REQ-SEC-001..004, REQ-SEC-007/008: cifra em repouso, índice cego, só código pseudônimo sai do portal, exportação de pesquisa pseudonimizada, log de acesso; REQ-SEC-010 *(planejado)*: HTTPS e cabeçalhos; REQ-SEC-011 *(planejado)*: direitos do titular | 5 | 1 | 5 (o tráfego HTTP do headset na LAN é risco aceito e documentado até REQ-SEC-010) | testes de REQ-SEC-001..004, REQ-SEC-007, REQ-SEC-008 (REQ-SEC-010/011 planejados) em `TRACEABILITY.md` |
| HAZ-008 | Dado de exame perdido ou alterado (gravação falha, arquivo editado, importação duplicada) | Exame irrecuperável; laudo baseado em dado adulterado | 4 | 2 | 8 | REQ-REC-003: HMAC por dispositivo verificado no headset e na importação, com estado de integridade no laudo; REQ-HS-006: falha de gravação mostrada ao operador; REQ-SYNC-004: fila com recibo; REQ-SEC-012 *(planejado)*: backup | 4 | 1 | 4 | testes de REQ-REC-003, REQ-HS-006, REQ-SYNC-004 (REQ-SEC-012 planejado) em `TRACEABILITY.md` |
| HAZ-009 | Comando remoto indevido (pausar/encerrar por terceiro, token vazado) | Exame interrompido; sessão inválida | 2 | 2 | 4 | REQ-SYNC-002/003/006: pareamento com código curto e token revogável, comandos limitados a pausar/retomar/encerrar, auditados; REQ-SEC-009 *(planejado)*: limite de tentativas | 2 | 1 | 2 | testes de REQ-SYNC-002, REQ-SYNC-003, REQ-SYNC-006 (REQ-SEC-009 planejado) em `TRACEABILITY.md` |
| HAZ-010 | Resultado mostrado ao paciente (ansiedade, autointerpretação) | Dano psicológico; quebra da relação médico-paciente | 2 | 2 | 4 | REQ-HS-004: nenhuma tela do headset mostra resultado; tela final neutra | 2 | 1 | 2 | testes de REQ-HS-004 em `TRACEABILITY.md` |
| HAZ-011 | Interpretação do normativo provisório como validado (mapas de probabilidade e índices sem validade clínica) | Conduta baseada em probabilidades sintéticas | 4 | 4 | 16 | REQ-ANA-002, REQ-PORT-004: estado PROVISÓRIO em todo exame e JSON; no laudo, nota de rodapé "NORMATIVO PROVISÓRIO — probabilidades e índices sem validade clínica" e a marca "normativo provisório" ao lado dos índices (por decisão do product owner de 2026-10-18, notas de rodapé e não faixas vermelhas); REQ-ANA-014 *(proposto, Fase 6)*: normativo próprio construído pelo `/core` a partir da coorte de voluntários substitui o provisório quando ativado; fica PROVISIONAL (aviso com N em todo laudo) abaixo de 100 olhos elegíveis | 4 | 2 | 8 (tolerável enquanto o uso é pesquisa sob supervisão) | testes de REQ-ANA-002, REQ-PORT-004, REQ-ANA-014 em `TRACEABILITY.md` |
| HAZ-012 | Fotossensibilidade, fadiga, náusea em RV (estímulos repetidos, flicker, headset mal ajustado) | Desconforto, crise em paciente suscetível | 3 | 2 | 6 | Fundo fotópico constante de 10 cd/m² (REQ-STIM-001), estímulos de 200 ms sem flicker; pausa a qualquer momento (REQ-REL-010); retirada do headset pausa; rastreio clínico de fotossensibilidade é responsabilidade do médico (instrução de uso a redigir) | 3 | 1 | 3 | testes de REQ-STIM-001, REQ-REL-010 em `TRACEABILITY.md` |
| HAZ-013 | Versão de software ou configuração diferente entre headset e análise (constante clínica alterada sem rastro) | Análise incoerente com o exame | 3 | 3 | 9 | REQ-REC-002/008: hash SHA-256 do `config/` em todo exame; laudo avisa quando o hash do exame difere do da análise; versões do headset e da análise no rodapé (REQ-PORT-012) | 3 | 1 | 3 | testes de REQ-REC-002, REQ-REC-008, REQ-PORT-012 em `TRACEABILITY.md` |
| HAZ-014 | Setores do GHT não validados (composição de `config/ght.json` TO_BE_VALIDATED) | Classificação hemicampo errada | 3 | 3 | 9 | REQ-ANA-007: algoritmo conforme Åsman & Heijl 1992, setores em arquivo versionado marcado TO_BE_VALIDATED, [DECISÃO PENDENTE] visível na especificação; GHT "não se aplica" no 10-2 | 3 | 2 | 6 | testes de REQ-ANA-007 em `TRACEABILITY.md` |
| HAZ-015 | Estratégia psicofísica divergente da referência (erro de porte do ZEST/4-2/FT) | Limiares sistematicamente deslocados | 4 | 2 | 8 | REQ-STRAT-001..006: portes do OPI com cross-check automatizado em R (140 cenários, 0 divergências) e PRNG determinístico | 4 | 1 | 4 | testes de REQ-STRAT-001..007 em `TRACEABILITY.md` |
| HAZ-016 | Modos experimentais usados como rotina (escolha forçada, modo B, foveal) sem entendimento das diferenças | Limiares não comparáveis ao HFA interpretados como tais | 3 | 3 | 9 | REQ-STRAT-010/011, REQ-REL-008: desligados por padrão, escolhidos pelo médico no pedido, declarados no laudo ("experimental"); documentados como padrão proposto | 3 | 1 | 3 | testes de REQ-STRAT-010, REQ-STRAT-011, REQ-REL-008 em `TRACEABILITY.md` |
| HAZ-017 | Exame interrompido analisado como completo | Índices calculados sobre campo parcial | 4 | 2 | 8 | REQ-ANA-010: `Aborted` nunca analisado como completo; importador recusa arquivo inválido | 4 | 1 | 4 | testes de REQ-ANA-010 em `TRACEABILITY.md` |
| HAZ-018 | Acesso não autorizado ao portal (senha fraca, sessão roubada, força bruta) | Exposição de dados de vários pacientes | 5 | 2 | 10 | REQ-SEC-005/006: scrypt, cookie httpOnly com token em hash, validade limitada; REQ-SEC-009/010 *(planejados)*: bloqueio de tentativas, HTTPS e cabeçalhos | 5 | 1 | 5 | testes de REQ-SEC-005, REQ-SEC-006 (REQ-SEC-009/010 planejados) em `TRACEABILITY.md` |

## 4. Riscos residuais a aceitar pelo product owner

1. **Sem calibração fotométrica** (HAZ-001) todos os resultados são em escala nominal; o uso clínico
   exige a tabela de calibração por dispositivo. O rótulo é o único controle até lá.
2. **Normativo provisório** (HAZ-011): probabilidades e índices sem validade clínica até o normativo
   próprio (Fase 6) atingir a população mínima.
3. **Tráfego do headset em HTTP na LAN** (HAZ-007/HAZ-009): risco residual **aceito pelo product owner em
   2026-10-18** ("comunicação simples neste momento"); a rede da clínica deve ser isolada; TLS fica para uma fase futura.
4. **Fixação no Quest 3** (HAZ-004): só Heijl-Krakau; sem eye-tracking o olhar não é medido.

## 5. Rastreabilidade

Cada controle cita o requisito em `SRS.md`; a coluna "Verificação" aponta para os testes que a matriz
`TRACEABILITY.md` (gerada por `tools/trace/build_matrix.py`) lista para esses requisitos, de modo que a
lista exata de testes por perigo vem sempre da matriz atual. Perigos cujo controle está *(planejado)*
devem ser reavaliados ao fim da Fase 6.
