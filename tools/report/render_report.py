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
    """Main lattice step: the largest gap between sorted coordinates seen at least three times (6° on the
    24-2/30-2/24-2C, 2° on the 10-2); else the smallest gap."""
    vs = sorted(set([p["X"] for p in pts] + [p["Y"] for p in pts]))
    d = [round(b - a, 3) for a, b in zip(vs, vs[1:]) if b - a > 1e-6]
    if not d:
        return 6
    frequent = [g for g in set(d) if d.count(g) >= 3]
    return max(frequent) if frequent else min(d)


def fine_spacing(pts):
    xs = sorted(set(p["X"] for p in pts))
    d = [b - a for a, b in zip(xs, xs[1:]) if b - a > 1e-6]
    return min(d) if d else 6


def on_lattice(v, s):
    """Lattice points sit at odd multiples of half the step (±3, ±9 … for 6°)."""
    r = (abs(v) + s / 2) % s
    return r < 1e-6 or abs(r - s) < 1e-6


def cell_of(p, s, fine):
    return s if on_lattice(p["X"], s) and on_lattice(p["Y"], s) else fine


def numeric(ax, title, pts, eye, key, fmt="{:.0f}", censored=False):
    s, fine = spacing(pts), fine_spacing(pts)
    for x, y, p in setup(ax, title, pts, eye):
        if p["IsBlindSpot"] and key in ("Td", "Pd"):
            continue
        v = num(p[key])
        txt = "<0" if censored and p.get("CensoredLow") else ("" if math.isnan(v) else fmt.format(v))
        ax.text(x, y, txt, ha="center", va="center", fontsize=7 if cell_of(p, s, fine) == s else 4.5)


def grayscale(ax, pts, eye, nbands=10):
    s, fine = spacing(pts), fine_spacing(pts)
    # main lattice cells first, then the fine (macular) cells of the 24-2C on top
    cells = sorted(setup(ax, "Escala de cinza (dB equiv. HFA)", pts, eye), key=lambda t: -cell_of(t[2], s, fine))
    for x, y, p in cells:
        c = cell_of(p, s, fine)
        g = p["GrayBand"] / (nbands - 1)
        ax.add_patch(Rectangle((x - c / 2, y - c / 2), c, c, color=str(g), lw=0))
    # Physiological blind spot marker, only on grids that reach it (not the 10-2).
    if ax.get_xlim()[1] > 18:
        bx = 15 if eye == "OD" else -15
        ax.add_patch(matplotlib.patches.Ellipse((bx, -1.5), 5.5, 7.5, color="0.75"))


def prob(ax, title, pts, eye, key):
    s0, fine = spacing(pts), fine_spacing(pts)
    for x, y, p in setup(ax, title, pts, eye):
        if p["IsBlindSpot"]:
            continue
        s = cell_of(p, s0, fine) * 0.55
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


def foveal_text(a):
    """'Limiar foveal' line (Phase 5): HFA-equivalent dB with its total-deviation p-mark, or 'não testado'."""
    if not a.get("FovealTested"):
        return "Limiar foveal: não testado"
    v = num(a.get("FovealHfaDb"))
    txt = "Limiar foveal: " + ("<" + f"{v:.0f}" if a.get("FovealCensoredLow") else f"{v:.0f}") + " dB"
    p = num(a.get("FovealTdP"))
    if not math.isnan(p) and p < 1:
        txt += "  p < " + f"{100 * p:g}".replace(".", ",") + "%"
    return txt


def gaze_bar(ax, rel, clip=10.0):
    """HFA-style gaze tracking strip (Phase 5): up = gaze error at stimulus onset (clipped at `clip` degrees),
    down = presentation rejected by the headset (tracking lost / error above the limit)."""
    trace = rel.get("GazeTrace") or []
    n = max(1, len(trace))
    ax.set_xlim(0, n); ax.set_ylim(-0.45, 1.05)
    ax.set_xticks([]); ax.set_yticks([0, 0.5, 1]); ax.set_yticklabels(["0°", f"{clip / 2:g}°", f"{clip:g}°"], fontsize=6)
    for s in ("top", "right", "bottom"): ax.spines[s].set_visible(False)
    ax.axhline(0, color="k", lw=0.8)
    for y in (0.5, 1.0): ax.axhline(y, color="0.6", lw=0.3)
    lim = rel.get("GazeMaxErrorDeg")
    if lim and 0 < lim < clip:
        ax.axhline(lim / clip, color="red", lw=0.5, ls="--")
    lw = max(0.3, min(1.5, 300 / n))
    for i, t in enumerate(trace):
        if t["Rejected"]:
            ax.vlines(i + 0.5, -0.4, 0, color="red", lw=lw)
        else:
            e = num(t.get("ErrorDeg"))
            ax.vlines(i + 0.5, 0, 0 if math.isnan(e) else min(1.0, max(0.0, e) / clip), color="k", lw=lw)
    mean = num(rel.get("GazeMeanErrorDeg"))
    ax.set_title(f"Rastreamento do olhar: erro médio {'—' if math.isnan(mean) else f'{mean:.1f}°'.replace('.', ',')}, "
                 f"{rel.get('GazeRejectedPresentations', 0)} descartadas ({rel.get('GazeSamples', len(trace))} rastreadas); ↓ descartada",
                 fontsize=7, loc="left", pad=3)


def main(analysis_path, out_png, exam_path=None):
    doc = json.load(open(analysis_path))
    a = doc["analysis"]
    eye, pts = a["Eye"], a["Points"]
    rel = a["Reliability"]
    gaze = bool(rel.get("GazeTracked"))
    fig = plt.figure(figsize=(8.27, 11.69), dpi=150)  # A4 portrait
    fig.text(0.05, 0.965, f"Análise de campo único — {a['GridId']} — Olho {'direito (OD)' if eye == 'OD' else 'esquerdo (OS)'}",
             fontsize=13, weight="bold")
    fl, fp, fn = rel["FixationLosses"], rel["FalsePositives"], rel["FalseNegatives"]
    pct = lambda c: "" if c["Trials"] == 0 else f" ({100 * c['Errors'] / c['Trials']:.0f}%)"
    dur = rel["DurationMs"] / 1000
    lines = [
        f"Exame: {a['ExamId']}    Idade: {a['AgeYears']:.0f}    Offset dB equiv. HFA: {a['HfaOffsetDb']:.1f} dB",
        f"Perdas de fixação: {fl['Errors']}/{fl['Trials']}{pct(fl)}    Falsos positivos: {fp['Errors']}/{fp['Trials']}{pct(fp)}"
        f"    Falsos negativos: {fn['Errors']}/{fn['Trials']}{pct(fn)}"
        + (f"    Olhar descartado: {rel.get('GazeRejectedPresentations', 0)}" if gaze else ""),
        f"Duração: {int(dur // 60):02d}:{int(dur % 60):02d}    Apresentações: {rel['TotalPresentations']}"
        f"    Mancha cega: {'localizada' if rel['BlindSpotFound'] else 'não localizada'}    {foveal_text(a)}",
    ]
    for i, l in enumerate(lines):
        fig.text(0.05, 0.94 - i * 0.017, l, fontsize=8)
    # Warnings go in a small footer note (product owner, 2026-10-18), not across the top of the page.
    fig.add_artist(plt.Line2D([0.05, 0.95], [0.036, 0.036], color="0.6", linewidth=0.5))
    notes = ["⚠ " + w for w in a["Warnings"]]
    for i in range(0, len(notes), 2):
        fig.text(0.05, 0.024 - (i // 2) * 0.012, "    ".join(notes[i:i + 2]), fontsize=6.5, color="#a00000")
    if gaze:
        gaze_bar(fig.add_axes([0.08, 0.045, 0.87, 0.035]), rel)  # just above the footer note

    screening = any(p.get("ScreeningOutcome") for p in pts)
    if screening:
        ax = fig.add_axes([0.1, 0.35, 0.8, 0.45])
        m = {"Seen": "o", "SeenOnRetest": "r", "NotSeen": "■"}
        for x, y, p in setup(ax, "Rastreio (o visto, r visto no reteste, ■ não visto)", pts, eye):
            ax.text(x, y, m.get(p.get("ScreeningOutcome"), "?"), ha="center", va="center", fontsize=9)
    else:
        # Three rows of panels; with the gaze strip they shrink so the legend and indices stay above it.
        top, w = 0.62, 0.42
        h = 0.21 if gaze else 0.24
        row = h + 0.01
        numeric(fig.add_axes([0.05, top, w, h]), "Limiares (dB dispositivo)", pts, eye, "DeviceDb", censored=True)
        grayscale(fig.add_axes([0.53, top, w, h]), pts, eye)
        numeric(fig.add_axes([0.05, top - row, w, h]), "Desvio total (dB)", pts, eye, "Td")
        numeric(fig.add_axes([0.53, top - row, w, h]), "Desvio padrão / Pattern deviation (dB)", pts, eye, "Pd")
        prob(fig.add_axes([0.05, top - 2 * row, w, h]), "Probabilidade — desvio total", pts, eye, "TdP")
        prob(fig.add_axes([0.53, top - 2 * row, w, h]), "Probabilidade — desvio padrão", pts, eye, "PdP")
        base = 0.095 if gaze else 0.045
        legend(fig.add_axes([0.05, base, 0.2, 0.07]))
        p = lambda v: "" if num(v) >= 1 else "  p < " + f"{100 * num(v):g}".replace(".", ",") + "%"
        ght = "Não se aplica" if a.get("GhtApplicable") is False else GHT_PT.get(a["Ght"], a["Ght"])
        idx = [f"GHT: {ght}",
               f"VFI: {num(a['Vfi']):.1f}%".replace(".", ","),
               f"MD: {num(a['Md']):.2f} dB{p(a['MdP'])}".replace(".", ",", 1),
               f"PSD: {num(a['Psd']):.2f} dB{p(a['PsdP'])}".replace(".", ",", 1)]
        for i, t in enumerate(idx):
            fig.text(0.40, base + 0.06 - i * 0.017, t, fontsize=9, weight="bold" if i == 0 else None)
    fig.savefig(out_png)
    print(f"wrote {out_png}")


if __name__ == "__main__":
    main(*sys.argv[1:])
