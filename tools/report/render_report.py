#!/usr/bin/env python3
"""
Renders an HFA-like "Single Field Analysis" PNG from the analysis JSON written by
`perimetry simulate --out exam.json` (exam.analysis.json) or `perimetry analyze exam.json --out a.json`.

Usage: python3 render_report.py exam.analysis.json report.png [exam.json]
Phase 1 tool; the A4 PDF in the HFA layout is phase 3 (portal).
"""
import json, math, sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

GHT_PT = {
    "WithinNormalLimits": "Dentro dos limites normais",
    "Borderline": "Limítrofe",
    "OutsideNormalLimits": "Fora dos limites normais",
    "GeneralReduction": "Sensibilidade geral reduzida",
    "BorderlineAndGeneralReduction": "Limítrofe / Sensibilidade geral reduzida",
    "AbnormallyHigh": "Sensibilidade anormalmente alta",
    "NotApplicable": "Não se aplica",
}


def num(v):
    return float("nan") if v in (None, "NaN") else float(v)


def setup(ax, title, pts, eye):
    xs = [p["X"] if eye == "OD" else -p["X"] for p in pts]
    ys = [p["Y"] for p in pts]
    lim = max(max(map(abs, xs)), max(map(abs, ys))) + 4
    ax.set_xlim(-lim, lim); ax.set_ylim(-lim, lim); ax.set_aspect("equal")
    ax.axhline(0, color="k", lw=0.8); ax.axvline(0, color="k", lw=0.8)
    ax.set_xticks([]); ax.set_yticks([])
    for s in ax.spines.values(): s.set_visible(False)
    ax.set_title(title, fontsize=9, pad=14)
    return [(x, y, p) for x, y, p in zip(xs, ys, pts)]


def spacing(pts):
    xs = sorted(set(p["X"] for p in pts))
    d = [b - a for a, b in zip(xs, xs[1:]) if b - a > 0]
    return min(d) if d else 6


def numeric(ax, title, pts, eye, key, fmt="{:.0f}", censored=False):
    for x, y, p in setup(ax, title, pts, eye):
        if p["IsBlindSpot"] and key in ("Td", "Pd"):
            continue
        v = num(p[key])
        txt = "<0" if censored and p.get("CensoredLow") else ("" if math.isnan(v) else fmt.format(v))
        ax.text(x, y, txt, ha="center", va="center", fontsize=7)


def grayscale(ax, pts, eye, nbands=10):
    s = spacing(pts)
    for x, y, p in setup(ax, "Escala de cinza (dB equiv. HFA)", pts, eye):
        g = p["GrayBand"] / (nbands - 1)
        ax.add_patch(Rectangle((x - s / 2, y - s / 2), s, s, color=str(g), lw=0))
    bx = 15 if eye == "OD" else -15
    ax.add_patch(matplotlib.patches.Ellipse((bx, -1.5), 5.5, 7.5, color="0.75"))


def prob(ax, title, pts, eye, key):
    s = spacing(pts) * 0.55
    for x, y, p in setup(ax, title, pts, eye):
        if p["IsBlindSpot"]:
            continue
        v = num(p[key])
        if v <= 0.005:
            ax.add_patch(Rectangle((x - s / 2, y - s / 2), s, s, color="black"))
        elif v <= 0.05:
            hatch = {0.01: "xxxx", 0.02: "xxx", 0.05: ".."}[min((0.01, 0.02, 0.05), key=lambda q: abs(q - v))]
            ax.add_patch(Rectangle((x - s / 2, y - s / 2), s, s, fill=False, hatch=hatch, lw=0.3))
        else:
            ax.plot(x, y, marker=".", color="k", ms=2)


def legend(ax):
    ax.axis("off")
    ax.set_xlim(0, 10); ax.set_ylim(0, 5)
    items = [("..", "p < 5%"), ("xxx", "p < 2%"), ("xxxx", "p < 1%"), (None, "p < 0,5%")]
    for i, (h, t) in enumerate(items):
        if h is None:
            ax.add_patch(Rectangle((0.3, 3.8 - i), 0.6, 0.6, color="black"))
        else:
            ax.add_patch(Rectangle((0.3, 3.8 - i), 0.6, 0.6, fill=False, hatch=h, lw=0.3))
        ax.text(1.2, 4.1 - i, t, va="center", fontsize=8)


def main(analysis_path, out_png, exam_path=None):
    doc = json.load(open(analysis_path))
    a = doc["analysis"]
    eye, pts = a["Eye"], a["Points"]
    rel = a["Reliability"]
    fig = plt.figure(figsize=(8.27, 11.69), dpi=150)  # A4 portrait
    fig.text(0.05, 0.965, f"Análise de campo único — {a['GridId']} — Olho {'direito (OD)' if eye == 'OD' else 'esquerdo (OS)'}",
             fontsize=13, weight="bold")
    fl, fp, fn = rel["FixationLosses"], rel["FalsePositives"], rel["FalseNegatives"]
    pct = lambda c: "" if c["Trials"] == 0 else f" ({100 * c['Errors'] / c['Trials']:.0f}%)"
    dur = rel["DurationMs"] / 1000
    lines = [
        f"Exame: {a['ExamId']}    Idade: {a['AgeYears']:.0f}    Offset dB equiv. HFA: {a['HfaOffsetDb']:.1f} dB",
        f"Perdas de fixação: {fl['Errors']}/{fl['Trials']}{pct(fl)}    Falsos positivos: {fp['Errors']}/{fp['Trials']}{pct(fp)}"
        f"    Falsos negativos: {fn['Errors']}/{fn['Trials']}{pct(fn)}",
        f"Duração: {int(dur // 60):02d}:{int(dur % 60):02d}    Apresentações: {rel['TotalPresentations']}"
        f"    Mancha cega: {'localizada' if rel['BlindSpotFound'] else 'não localizada'}",
    ]
    for i, l in enumerate(lines):
        fig.text(0.05, 0.94 - i * 0.017, l, fontsize=8)
    for i, w in enumerate(a["Warnings"]):
        fig.text(0.05, 0.885 - i * 0.015, "⚠ " + w, fontsize=8, color="red", weight="bold")

    if a["Ght"] == "NotApplicable":
        ax = fig.add_axes([0.1, 0.35, 0.8, 0.45])
        m = {"Seen": "o", "SeenOnRetest": "r", "NotSeen": "■"}
        for x, y, p in setup(ax, "Rastreio (o visto, r visto no reteste, ■ não visto)", pts, eye):
            ax.text(x, y, m.get(p.get("ScreeningOutcome"), "?"), ha="center", va="center", fontsize=9)
    else:
        # Panels shrink so their titles stay below the last warning line.
        top, w = 0.58, 0.42
        h = min(0.24, 0.885 - len(a["Warnings"]) * 0.015 - 0.015 - top)
        numeric(fig.add_axes([0.05, top, w, h]), "Limiares (dB dispositivo)", pts, eye, "DeviceDb", censored=True)
        grayscale(fig.add_axes([0.53, top, w, h]), pts, eye)
        numeric(fig.add_axes([0.05, top - 0.25, w, h]), "Desvio total (dB)", pts, eye, "Td")
        numeric(fig.add_axes([0.53, top - 0.25, w, h]), "Desvio padrão / Pattern deviation (dB)", pts, eye, "Pd")
        prob(fig.add_axes([0.05, top - 0.50, w, h]), "Probabilidade — desvio total", pts, eye, "TdP")
        prob(fig.add_axes([0.53, top - 0.50, w, h]), "Probabilidade — desvio padrão", pts, eye, "PdP")
        legend(fig.add_axes([0.05, 0.005, 0.2, 0.07]))
        p = lambda v: "" if num(v) >= 1 else "  p < " + f"{100 * num(v):g}".replace(".", ",") + "%"
        idx = [f"GHT: {GHT_PT[a['Ght']]}",
               f"VFI: {num(a['Vfi']):.1f}%".replace(".", ","),
               f"MD: {num(a['Md']):.2f} dB{p(a['MdP'])}".replace(".", ",", 1),
               f"PSD: {num(a['Psd']):.2f} dB{p(a['PsdP'])}".replace(".", ",", 1)]
        for i, t in enumerate(idx):
            fig.text(0.40, 0.065 - i * 0.017, t, fontsize=9, weight="bold" if i == 0 else None)
        if a.get("NormativeProvisional"):
            fig.text(0.40, -0.0 + 0.002, "Índices e probabilidades com normativo PROVISÓRIO", fontsize=7, color="red")
    fig.savefig(out_png)
    print(f"wrote {out_png}")


if __name__ == "__main__":
    main(*sys.argv[1:])
