import type { Eye, PointAnalysis } from "@/lib/report-types";

// SVG field maps in the HFA convention: the field as the patient sees it (OS mirrored), x right / y up,
// thresholds and deviations as numbers, grayscale as 10 dither patterns (0 dB black … ≥ 36 dB white),
// probability symbols p < 5% / 2% / 1% / 0.5% as increasingly dense patterns.

const BAYER = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

/** Anything with a grid position (report points or live locations). */
export interface MapPoint { Index: number; X: number; Y: number }

/**
 * `spacing` is the main lattice step (6° on 24-2 / 30-2 / 24-2C, 2° on 10-2); `cell` per point is the square it
 * owns: the main step for lattice points, the fine step (`fine`, 2° on the 24-2C) for the extra macular points.
 */
export interface MapGeometry<P extends MapPoint = PointAnalysis> { points: (P & { dx: number; cell: number })[]; lim: number; spacing: number; fine: number }

/** Main lattice step: the largest gap between sorted coordinates that occurs at least three times (else the smallest). */
export function latticeStep(values: number[]): number {
  const xs = [...new Set(values.map((v) => Math.round(v * 1000) / 1000))].sort((a, b) => a - b);
  const gaps = xs.slice(1).map((x, i) => Math.round((x - xs[i]) * 1000) / 1000).filter((g) => g > 1e-6);
  if (!gaps.length) return 6;
  const count = new Map<number, number>();
  for (const g of gaps) count.set(g, (count.get(g) ?? 0) + 1);
  const frequent = [...count.entries()].filter(([, n]) => n >= 3).map(([g]) => g);
  return frequent.length ? Math.max(...frequent) : Math.min(...gaps);
}

export function geometry<P extends MapPoint>(points: P[], eye: Eye): MapGeometry<P> {
  const mirrored = points.map((p) => ({ ...p, dx: eye === "OS" ? -p.X : p.X }));
  const spacing = latticeStep(mirrored.flatMap((p) => [p.dx, p.Y]));
  const xs = [...new Set(mirrored.map((p) => p.dx))].sort((a, b) => a - b);
  const gaps = xs.slice(1).map((x, i) => x - xs[i]).filter((g) => g > 1e-6);
  const fine = gaps.length ? Math.min(...gaps) : spacing;
  // lattice points sit at odd multiples of half the step (±3, ±9 … for 6°; ±1, ±3 … for 2°)
  const onLattice = (v: number) => Math.abs(((Math.abs(v) + spacing / 2) % spacing) - 0) < 1e-6 || Math.abs(((Math.abs(v) + spacing / 2) % spacing) - spacing) < 1e-6;
  const pts = mirrored.map((p) => ({ ...p, cell: onLattice(p.dx) && onLattice(p.Y) ? spacing : fine }));
  const lim = Math.max(...pts.map((p) => Math.max(Math.abs(p.dx), Math.abs(p.Y)))) + spacing / 2 + 1;
  return { points: pts, lim, spacing, fine };
}

/** Font / symbol scale of a point: the main step for lattice points, smaller for the crowded macular extras. */
function cellScale(g: MapGeometry<MapPoint>, p: { cell: number }): number {
  return p.cell === g.spacing ? g.spacing : Math.max(g.fine * 0.75, g.spacing * 0.25);
}

/** Axis tick step: every 10° on the 24-2 / 30-2 / 24-2C frames, every 5° on the macular 10-2 frame. */
export function tickStep(lim: number): number {
  return lim <= 15 ? 5 : 10;
}

export function Frame({ g, children, title, id }: { g: MapGeometry<MapPoint>; children: React.ReactNode; title: string; id: string }) {
  const L = g.lim;
  const step = tickStep(L);
  const ticks: number[] = [];
  for (let t = -40; t <= 40; t += step) if (t !== 0 && Math.abs(t) < L) ticks.push(t);
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
  return (
    <Frame g={g} title={title} id={id}>
      {g.points.map((p) => {
        const v = value(p);
        return v === "" ? null : (
          <text key={p.Index} x={p.dx} y={-p.Y} fontSize={cellScale(g, p) * 0.42} textAnchor="middle" dominantBaseline="central" className="num">{v}</text>
        );
      })}
    </Frame>
  );
}

/**
 * Ordered-dither fill of one cell, as a single path of black runs on a global lattice of size/20 (0.3° on the
 * 6° grids, 0.1° on the 2° cells of the 10-2), so adjacent cells continue the same Bayer pattern like the HFA
 * printout. Band 9 is white, band 0 black. Drawn as geometry rather than SVG <pattern>: Chromium's PDF output
 * expands patterns into megabytes.
 */
export function ditherPath(x0: number, y0: number, size: number, band: number): string {
  const k = Math.round(((9 - band) / 9) * 16);
  if (k === 0) return "";
  const q = Math.round((size / 20) * 1000) / 1000, n = Math.round(size / q), i0 = Math.round(x0 / q), j0 = Math.round(y0 / q);
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
  // main lattice cells first, then the fine (macular) cells on top of them
  const ordered = [...g.points].sort((a, b) => b.cell - a.cell);
  return (
    <Frame g={g} title={title} id={id}>
      {ordered.map((p) => {
        const s = p.cell;
        const fine = s !== g.spacing;
        return (
          <g key={p.Index}>
            {fine && <rect x={p.dx - s / 2} y={-p.Y - s / 2} width={s} height={s} fill="#fff" />}
            <path d={ditherPath(p.dx - s / 2, -p.Y - s / 2, s, p.GrayBand) || "M0 0"} fill="#000" data-band={p.GrayBand} />
          </g>
        );
      })}
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
  return (
    <Frame g={g} title={title} id={id}>
      <ProbPatterns prefix={id} />
      {g.points.map((p) => {
        if (p.IsBlindSpot) return null;
        const lv = probLevel(value(p));
        return lv ? <ProbSymbol key={p.Index} level={lv} x={p.dx} y={-p.Y} size={cellScale(g, p) * 0.62} prefix={id} /> : null;
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
  return (
    <Frame g={g} title={title} id={id}>
      {g.points.map((p) => {
        const s = cellScale(g, p) * 0.5;
        const o = p.ScreeningOutcome;
        if (o === "NotSeen") return <rect key={p.Index} x={p.dx - s / 2} y={-p.Y - s / 2} width={s} height={s} fill="#000" />;
        if (o === "SeenOnRetest") return <text key={p.Index} x={p.dx} y={-p.Y} fontSize={s} textAnchor="middle" dominantBaseline="central">r</text>;
        if (o === "Seen") return <circle key={p.Index} cx={p.dx} cy={-p.Y} r={s / 3} fill="none" stroke="#000" strokeWidth={0.25} />;
        return null;
      })}
    </Frame>
  );
}
