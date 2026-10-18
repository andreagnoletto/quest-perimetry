#!/usr/bin/env python3
"""Builds docs/regulatory/TRACEABILITY.md from docs/regulatory/traceability.json and checks it.

Usage: python3 tools/trace/build_matrix.py [--check-only]

traceability.json: { "<REQ-id>": { "title": "...", "status": "implemented|proposed|planned",
                                   "impl": ["path", "path#Symbol", ...],
                                   "tests": ["NUnit method name" | "vitest/playwright title", ...],
                                   "scripts": ["tools/.../run.sh", ...] } }

Checks (exit code 1 on any failure):
  * every "impl" path exists (the part before "#" is the path; "#Symbol" is documentation only);
  * every "tests" entry occurs verbatim in a test source file (NUnit method name or test title);
  * every "scripts" entry exists;
  * every requirement whose status is not "planned" has at least one test or script;
  * every requirement id appears in docs/regulatory/SRS.md (the matrix and the SRS stay in step).
Only the Python standard library is used.
"""
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MATRIX_JSON = os.path.join(ROOT, "docs", "regulatory", "traceability.json")
MATRIX_MD = os.path.join(ROOT, "docs", "regulatory", "TRACEABILITY.md")
TEST_DIRS = [
    ("core/Perimetry.Core.Tests", (".cs",)),
    ("headset/LogicTests", (".cs",)),
    ("headset/Assets/Perimetry/Tests", (".cs",)),
    ("portal/tests", (".ts",)),
]
AREA_ORDER = ["STIM", "STRAT", "REL", "ANA", "REC", "HS", "SYNC", "PORT", "SEC", "DOC"]
SRS_MD = os.path.join(ROOT, "docs", "regulatory", "SRS.md")
STATUS_PT = {"implemented": "implementado", "proposed": "padrão proposto (aguarda confirmação)", "planned": "planejado"}


def load_test_sources():
    sources = {}
    for d, exts in TEST_DIRS:
        base = os.path.join(ROOT, d)
        for dirpath, _, files in os.walk(base):
            for f in files:
                if f.endswith(exts):
                    p = os.path.join(dirpath, f)
                    with open(p, encoding="utf-8") as fh:
                        sources[os.path.relpath(p, ROOT)] = fh.read()
    return sources


def find_test(name, sources):
    """Returns the files that contain the test name verbatim (NUnit method or vitest/playwright title)."""
    hits = []
    for path, text in sources.items():
        if f"void {name}(" in text or f'"{name}"' in text or f"'{name}'" in text or f"`{name}`" in text:
            hits.append(path)
    return hits


def main(argv):
    check_only = "--check-only" in argv
    with open(MATRIX_JSON, encoding="utf-8") as fh:
        matrix = json.load(fh)
    sources = load_test_sources()
    errors, rows = [], []
    counts = {"implemented": 0, "proposed": 0, "planned": 0}
    tests_used = set()
    def order(req):
        area = req.split("-")[1] if req.count("-") >= 2 else ""
        return (AREA_ORDER.index(area) if area in AREA_ORDER else len(AREA_ORDER), req)

    srs = open(SRS_MD, encoding="utf-8").read() if os.path.exists(SRS_MD) else ""
    for req in sorted(matrix, key=order):
        if req not in srs:
            errors.append(f"{req}: not mentioned in docs/regulatory/SRS.md")
        r = matrix[req]
        status = r.get("status", "implemented")
        if status not in STATUS_PT:
            errors.append(f"{req}: unknown status {status!r}")
            status = "implemented"
        counts[status] += 1
        for impl in r.get("impl", []):
            path = impl.split("#", 1)[0]
            if not os.path.exists(os.path.join(ROOT, path)):
                errors.append(f"{req}: implementation path not found: {path}")
        test_cells = []
        for t in r.get("tests", []):
            hits = find_test(t, sources)
            if not hits:
                errors.append(f"{req}: test not found in any test source: {t}")
                test_cells.append(f"`{t}` (NÃO ENCONTRADO)")
            else:
                tests_used.add(t)
                test_cells.append(f"`{t}` ({os.path.basename(hits[0])})")
        for s in r.get("scripts", []):
            if not os.path.exists(os.path.join(ROOT, s)):
                errors.append(f"{req}: script not found: {s}")
            test_cells.append(f"script `{s}`")
        if status != "planned" and not test_cells:
            errors.append(f"{req}: no test or script (status {status})")
        rows.append((req, r.get("title", ""), STATUS_PT[status],
                     "<br>".join(f"`{i}`" for i in r.get("impl", [])) or "—",
                     "<br>".join(test_cells) or "— (planejado)"))

    if not check_only:
        with open(MATRIX_MD, "w", encoding="utf-8") as fh:
            fh.write("# Matriz de rastreabilidade requisito → implementação → teste\n\n")
            fh.write("> **Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA.** "
                     "Este documento não afirma nem implica aprovação regulatória.\n\n")
            fh.write("Gerado por `python3 tools/trace/build_matrix.py` a partir de `docs/regulatory/traceability.json`; "
                     "não edite à mão. Os requisitos estão em `SRS.md`; os nomes de teste são os métodos NUnit "
                     "(`core/Perimetry.Core.Tests`, `headset/LogicTests`) e os títulos dos testes vitest/Playwright "
                     "(`portal/tests`). O script falha quando um caminho, teste ou script citado não existe, "
                     "ou quando um requisito não planejado fica sem verificação.\n\n")
            fh.write(f"Requisitos: {sum(counts.values())} (implementados {counts['implemented']}, "
                     f"padrão proposto {counts['proposed']}, planejados {counts['planned']}); "
                     f"testes distintos citados: {len(tests_used)}.\n\n")
            fh.write("| Requisito | Título | Status | Implementação | Verificação |\n|---|---|---|---|---|\n")
            for req, title, status, impl, tests in rows:
                fh.write(f"| {req} | {title} | {status} | {impl} | {tests} |\n")
    print(f"{sum(counts.values())} requirements, {len(tests_used)} distinct tests cited, {len(errors)} errors")
    for e in errors:
        print("  ERROR " + e)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
