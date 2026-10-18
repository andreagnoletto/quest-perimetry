import type { Eye, PointAnalysis } from "@/lib/report-types";

// SVG field maps in the HFA convention: the field as the patient sees it (OS mirrored), x right / y up,
// thresholds and deviations as numbers, grayscale as 10 dither patterns (0 dB black … ≥ 36 dB white),
// probability symbols p < 5% / 2% / 1% / 0.5% as increasingly dense patterns.

const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

/** Anything with a grid position (report points or live locations). */
export interface MapPoint { Index: number; X: number; Y: number }

export interface MapGeometry<P extends MapPoint = PointAnalysis> { points: (P & { dx: number })[]; lim: number; spacing: number }

export function geometry<P extends MapPoint>(points: P[], eye: Eye): MapGeometry<P> {
  const pts = points.map((p) => ({ ...p, dx: eye === "OS" ? -p.X : p.X }));
  const xs = [...new Set(pts.map((p) => p.dx))].sort((a, b) => a - b);
  const gaps = xs.slice(1).map((x, i) => x - xs[i]).filter((g) => g > 1e-6);
  const spacing = gaps.length ? Math.min(...gaps) : 6;
  const lim = Math.max(...pts.map((p) => Math.max(Math.abs(p.dx), Math.abs(p.Y)))) + spacing / 2 + 1;
  return { points: pts, lim, spacing };
}

export function Frame({ g, children, title, id }: { g: MapGeometry<MapPoint>; children: React.ReactNode; title: string; id: string }) {
  const L = g.lim;
  const ticks: number[] = [];
  for (let t = -30; t <= 30; t += 10) if (t !== 0 && Math.abs(t) < L) ticks.push(t);
  return (
    <figure className="map" aria-label={title} data-map={id}>
      <figcaption>{title}</figcaption>
      <svg viewBox={`${-L} ${-L} ${2 * L} ${2 * L}`} role="img">
        <line x1={-L} y1={0} x2={L} y2={0} className="axis" />
        <line x1={0} y1={-L} x2={0} y2={L} className="axis" />
        {ticks.map((t) => (
          <g key={t}>
            <line x1={t} y1={-0.6} x2={t} y2={0.6} className="axis" />
            <line x1={-0.6} y1={-t} x2={0.6} y2={-t} className="axis" />
          </g>
        ))}
        {children}
      </svg>
    </figure>
  );
}

export function NumericMap<P extends MapPoint>({ g, title, id, value }: { g: MapGeometry<P>; title: string; id: string; value: (p: P) => string }) {
  const fs = g.spacing * 0.42;
  return (
    <Frame g={g} title={title} id={id}>
      {g.points.map((p) => {
        const v = value(p);
        return v === "" ? null : (
          <text key={p.Index} x={p.dx} y={-p.Y} fontSize={fs} textAnchor="middle" dominantBaseline="central" className="num">{v}</text>
        );
      })}
    </Frame>
  );
}

/**
 * Ordered-dither fill of one cell, as a single path of black runs on a global 0.3° lattice (adjacent cells
 * continue the same Bayer pattern, like the HFA printout). Band 9 is white, band 0 black. Drawn as geometry
 * rather than SVG <pattern>: Chromium's PDF output expands patterns into megabytes.
 */
export function ditherPath(x0: number, y0: number, size: number, band: number): string {
  const k = Math.round(((9 - band) / 9) * 16);
  if (k === 0) return "";
  const q = 0.3, n = Math.round(size / q), i0 = Math.round(x0 / q), j0 = Math.round(y0 / q);
  const r = (v: number) => Math.round(v * 100) / 100;
  let d = "";
  for (let j = j0; j < j0 + n; j++) {
    let run: number | null = null; // lattice indices can be negative, so no -1 sentinel
    for (let i = i0; i <= i0 + n; i++) {
      const on = i < i0 + n && BAYER[(((j % 4) + 4) % 4) * 4 + (((i % 4) + 4) % 4)] < k;
      if (on && run === null) run = i;
      if (!on && run !== null) { d += `M${r(run * q)} ${r(j * q)}h${r((i - run) * q)}v${q}h${r(-(i - run) * q)}z`; run = null; }
    }
  }
  return d;
}

export function GrayscaleMap({ g, title, id }: { g: MapGeometry; title: string; id: string }) {
  const s = g.spacing;
  return (
    <Frame g={g} title={title} id={id}>
      {g.points.map((p) => (
        <path key={p.Index} d={ditherPath(p.dx - s / 2, -p.Y - s / 2, s, p.GrayBand) || "M0 0"} fill="#000" data-band={p.GrayBand} />
      ))}
    </Frame>
  );
}

export type ProbLevel = "ns" | "p5" | "p2" | "p1" | "p05";

/** Maps a /core probability (0.005, 0.01, 0.02, 0.05 or 1) to the HFA symbol class. */
export function probLevel(p: number | null): ProbLevel | null {
  if (p === null) return null;
  if (p <= 0.005) return "p05";
  if (p <= 0.01) return "p1";
  if (p <= 0.02) return "p2";
  if (p <= 0.05) return "p5";
  return "ns";
}

function ProbPatterns({ prefix }: { prefix: string }) {
  const cell = 0.8;
  const dots = (n: number) => BAYER.map((b, i) => (b < n ? <rect key={i} x={(i % 4) * (cell / 4)} y={Math.floor(i / 4) * (cell / 4)} width={cell / 8} height={cell / 8} fill="#000" /> : null));
  return (
    <defs>
      <pattern id={`${prefix}-p5`} width={cell} height={cell} patternUnits="userSpaceOnUse">{dots(4)}</pattern>
      <pattern id={`${prefix}-p2`} width={cell} height={cell} patternUnits="userSpaceOnUse">{dots(8)}</pattern>
      <pattern id={`${prefix}-p1`} width={cell / 2} height={cell / 2} patternUnits="userSpaceOnUse">
        <rect width={cell / 4} height={cell / 4} fill="#000" />
        <rect x={cell / 4} y={cell / 4} width={cell / 4} height={cell / 4} fill="#000" />
      </pattern>
    </defs>
  );
}

export function ProbSymbol({ level, x, y, size, prefix }: { level: ProbLevel; x: number; y: number; size: number; prefix: string }) {
  if (level === "ns") return <circle cx={x} cy={y} r={size * 0.07} fill="#000" />;
  if (level === "p05") return <rect x={x - size / 2} y={y - size / 2} width={size} height={size} fill="#000" />;
  return <rect x={x - size / 2} y={y - size / 2} width={size} height={size} fill={`url(#${prefix}-${level})`} stroke="#000" strokeWidth={size * 0.02} />;
}

export function ProbabilityMap({ g, title, id, value }: { g: MapGeometry; title: string; id: string; value: (p: PointAnalysis) => number | null }) {
  const size = g.spacing * 0.62;
  return (
    <Frame g={g} title={title} id={id}>
      <ProbPatterns prefix={id} />
      {g.points.map((p) => {
        if (p.IsBlindSpot) return null;
        const lv = probLevel(value(p));
        return lv ? <ProbSymbol key={p.Index} level={lv} x={p.dx} y={-p.Y} size={size} prefix={id} /> : null;
      })}
    </Frame>
  );
}

export function ProbabilityLegend({ id = "legend" }: { id?: string }) {
  const items: [ProbLevel, string][] = [["p5", "p < 5%"], ["p2", "p < 2%"], ["p1", "p < 1%"], ["p05", "p < 0,5%"]];
  return (
    <div className="legend">
      {items.map(([lv, label]) => (
        <span key={lv} className="legend-item">
          <svg viewBox="-0.6 -0.6 1.2 1.2" width="14" height="14">
            <ProbPatterns prefix={`${id}-${lv}`} />
            <ProbSymbol level={lv} x={0} y={0} size={1} prefix={`${id}-${lv}`} />
          </svg>
          {label}
        </span>
      ))}
    </div>
  );
}

export function ScreeningMap({ g, title, id }: { g: MapGeometry; title: string; id: string }) {
  const s = g.spacing * 0.5;
  return (
    <Frame g={g} title={title} id={id}>
      {g.points.map((p) => {
        const o = p.ScreeningOutcome;
        if (o === "NotSeen") return <rect key={p.Index} x={p.dx - s / 2} y={-p.Y - s / 2} width={s} height={s} fill="#000" />;
        if (o === "SeenOnRetest") return <text key={p.Index} x={p.dx} y={-p.Y} fontSize={s} textAnchor="middle" dominantBaseline="central">r</text>;
        if (o === "Seen") return <circle key={p.Index} cx={p.dx} cy={-p.Y} r={s / 3} fill="none" stroke="#000" strokeWidth={0.25} />;
        return null;
      })}
    </Frame>
  );
}
