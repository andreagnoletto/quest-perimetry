# Quest Perimetry

> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.**
> Não é um dispositivo médico aprovado.

Perimetria computadorizada estática automatizada (SAP) para Meta Quest 3 / 3S, com saída no formato
Single Field Analysis do Humphrey Field Analyzer. Especificação: [`SPEC.md`](SPEC.md).
Psicofísica implementada: [`docs/CLINICAL_SPEC.md`](docs/CLINICAL_SPEC.md).

## Estado
- **Fase 1 — `/core`** (biblioteca C# de psicofísica, simulador, análise, testes): concluída, aguardando revisão clínica.
- Fases 2–6 (headset Unity, portal, sincronização, eye-tracking, normativo próprio): não iniciadas.

## Começar
Requisitos: .NET 8 SDK. Opcional: Python 3 + matplotlib (relatório PNG), R + jsonlite (cross-check com o OPI).
```bash
dotnet test core/Perimetry.Core.Tests
dotnet run --project core/Perimetry.Cli -c Release -- simulate --strategy ZestFast --defect SuperiorArcuate --out out/exame.json
python3 tools/report/render_report.py out/exame.analysis.json out/exame.png
```
Exemplos de relatório em [`docs/examples/`](docs/examples). Comandos e convenções em [`CLAUDE.md`](CLAUDE.md).

## Licenças de terceiros
Partes portadas do OPI e do visualFields (Apache-2.0): ver [`NOTICE`](NOTICE) e [`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md).
A licença deste projeto ainda não foi definida pelo proprietário.
