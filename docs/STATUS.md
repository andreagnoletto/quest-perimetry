# Estado do projeto e retomada

Atualizado em 2026-10-20. Leia junto com `CLAUDE.md`, `SPEC.md` e `docs/CLINICAL_SPEC.md`.

## Onde estamos
- As **fases 1 a 6** do SPEC §9 estão na `main` (merge do PR #5 em `ed24199`). PRs #1–#5 mergeados; os ramos
  `phase-2-headset` … `phase-6-normative` ficaram só como histórico.
- Versão do núcleo gravada nos exames: `ExamSession.Version = "0.3.0-phase5"`. Hash de config no último APK: `b2501b6f`.
- Testes: 140 (`core/Perimetry.Core.Tests`), 57 (`headset/LogicTests`), 60 unitários + 5 specs Playwright (`portal`),
  cross-check OPI 140/140, matriz de rastreabilidade sem erros (`python3 tools/trace/build_matrix.py`).

## Headset do produto owner (Quest 3)
- APK instalado por cabo a partir de `262c1c4` (fase 5; a `main` contém esse commit). Pendente: gerar APK da `main` com o
  texto maior das mensagens (2026-10-20: `MessageLayout`, `headset.json › presentation.messageOffsetDeg/-LineHeightDeg/-MaxWidthDeg`).
- Abrir o app: atalho **"Abrir Quest Perimetry"** na área de trabalho do PC (cabo ligado) ou Biblioteca →
  seletor "Todos" → **"Fontes desconhecidas"**. A Biblioteca do Horizon OS às vezes não indexa apps instalados por cabo;
  reiniciar o headset resolve. Caminho definitivo: canal de teste privado na Meta Horizon Store (release channel),
  que exige o app criado na conta de desenvolvedor do dono em developer.meta.com; depois gerar APK assinado com
  versão incrementada.
- Exames reais ficam **só no PC do dono**, em `out/quest-exams` (gitignored), com um `LEIA-ME.md` local listando exames e
  relatórios. Nunca entram no repositório.
- Teste de campo 2026-10-19 (preset `24-2-zest-fast-foveal`, dois olhos): fluxo completo ok, confiabilidade boa,
  sem rastreamento ocular (Quest 3). Observação técnica: o limiar foveal parou com 3 estímulos válidos pelo critério
  do ZEST rápido (ver decisão pendente abaixo).

## Decisões pendentes (perguntar ao product owner)
1. **Critério de parada do limiar foveal**: hoje segue a estratégia do exame (`strategies.json › foveal.strategy = "exam"`).
   Proposta: usar sempre o critério do ZEST padrão (SD < 1,5 dB) na fóvea, mesmo em exames ZEST rápido.
2. Headset (`docs/CLINICAL_SPEC.md` §11): pausa, treino, alvo de fixação, duração a 72 Hz.
3. Padrões propostos da fase 5 (§4–§6.4): alvo diamante e `fovealExtraDb`, limite de olhar 3°, oculocinético ±10° /
   600 ms / 300 ms, N e parada da escolha forçada. Eye tracking no Quest Pro ainda não verificado em aparelho real.
4. Página demonstrativa para o paciente no portal (nome, nascimento, vídeo): adiada ("depois").
5. Publicação em release channel da Meta (item acima) e calibração fotométrica real (relatório segue "NÃO CALIBRADO").
6. Instalar o portal (Next.js + PostgreSQL) no PC do dono; até aqui ele só rodou no ambiente de desenvolvimento.

## Riscos residuais conhecidos
- `npm audit` no portal aponta avisos que só saem com Next 16 / Prisma 7 (`portal/SECURITY.md`).
- `headset/UnityCompileCheck` não compila o ramo `ENABLE_INPUT_SYSTEM` de `GazeTracking` (sem DLL do Input System no NuGet).
- Tráfego headset → portal em HTTP na LAN aceito pelo dono em 2026-10-18 (`docs/regulatory/RISK.md`, HAZ-014).
- `ProbabilityMap` ainda usa `<pattern>` SVG (PDF maior).

## Para retomar
```bash
dotnet build Perimetry.sln && dotnet test core/Perimetry.Core.Tests && dotnet test headset/LogicTests
cd portal && npm ci && npx prisma generate && npm run typecheck && npm test
```
Próximos passos sugeridos, na ordem: (a) mais exames de campo para validação; (b) decidir o item 1 e, se aprovado,
implementar com teste e ajuste em `config/strategies.json`; (c) gerar novo APK da `main` com versão incrementada;
(d) escolher o próximo bloco de trabalho com o dono (não há fase 7 no SPEC).
