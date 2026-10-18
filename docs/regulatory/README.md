# Documentação regulatória inicial (Fase 6)

> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.**
> Nada nesta pasta afirma ou implica aprovação regulatória de nenhuma autoridade. É o ponto de partida
> de um futuro dossiê técnico, escrito a partir do que está implementado e da especificação do product
> owner clínico (`SPEC.md`).

## Documentos

| Arquivo | Conteúdo | Como mantê-lo |
|---|---|---|
| `SRS.md` | Especificação de requisitos de software: requisitos numerados `REQ-<ÁREA>-<nnn>` com fonte, status e chave de configuração | Editar à mão. Todo requisito citado em `traceability.json` precisa aparecer aqui (o script verifica). |
| `RISK.md` | Gestão de risco simplificada no espírito da ISO 14971: perigos `HAZ-nnn`, severidade × probabilidade, controles ligados aos requisitos, risco residual | Editar à mão; reavaliar ao fim de cada fase. A severidade e a aceitação são do product owner clínico. |
| `traceability.json` | Fonte da matriz: por requisito, caminhos de implementação, nomes de teste (métodos NUnit, títulos vitest/Playwright) e scripts de verificação | Editar à mão ao criar ou renomear requisitos e testes. |
| `TRACEABILITY.md` | Matriz requisito → implementação → teste, **gerada** | Nunca editar; regenerar com o comando abaixo. |

## Regenerar e verificar a matriz

```bash
python3 tools/trace/build_matrix.py            # regenera TRACEABILITY.md e verifica
python3 tools/trace/build_matrix.py --check-only
```

O script usa só a biblioteca padrão do Python 3 e falha (código 1) quando um caminho de implementação
não existe, um nome de teste não é encontrado nos fontes de teste (`core/Perimetry.Core.Tests`,
`headset/LogicTests`, `headset/Assets/Perimetry/Tests`, `portal/tests`), um script citado não existe,
um requisito não planejado fica sem verificação ou um requisito não consta da `SRS.md`. A integração
contínua (`.github/workflows/core.yml`, job `traceability`) roda a verificação a cada push.

## Uso pretendido (rascunho, a confirmar pelo product owner)

Medir e documentar a sensibilidade luminosa diferencial do campo visual central (até 30°) de pacientes
adultos, em perimetria estática automatizada acromática num headset Meta Quest, sob supervisão de
oftalmologista, para pesquisa e apoio à avaliação clínica. O sistema não emite diagnóstico. Enquanto não
houver calibração fotométrica do display e normativo próprio medido, os resultados são em escala nominal
e as probabilidades não têm validade clínica; o laudo informa ambos.

## Premissas de classificação

- **IEC 62304**: classe de segurança de software **B** assumida (ver `RISK.md` §1); a confirmar.
- **ISO 14971**: análise limitada ao software e ao uso em pesquisa; não cobre usabilidade formal
  (IEC 62366-1), compatibilidade eletromagnética nem biocompatibilidade do headset (produto de consumo
  de terceiro).
- **LGPD**: controles em `SRS.md` §3.9 (SEC) e `RISK.md` HAZ-007/HAZ-018.

## O que ainda não está coberto

- Validação clínica (comparação com perímetro de referência em pacientes) e validação do normativo.
- Instruções de uso para operador e paciente; rastreio de fotossensibilidade.
- Plano e relatório de verificação e validação formais (hoje: testes automatizados e a matriz).
- Gestão de configuração e de versões de lançamento (hoje: git, hash do `config/` em cada exame).
- Registro de anomalias e ações corretivas (hoje: issues do repositório).
- Avaliação de usabilidade, análise de software de terceiros (SOUP) além de `docs/THIRD_PARTY.md`.
