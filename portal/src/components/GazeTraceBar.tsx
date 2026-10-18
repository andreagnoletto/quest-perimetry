import type { GazeBar } from "@/lib/report-format";
import { gazeCaption } from "@/lib/report-format";

/**
 * HFA-style gaze tracking strip (Phase 5): one thin tick per presentation, in exam order. Upward bars are the
 * gaze error at stimulus onset (clipped at 10°, scale 0 / 5 / 10°); a full-height downward mark is a presentation
 * the headset rejected (tracking lost or error above the limit), like the HFA's "down" marks. Plain SVG
 * geometry only, so Chromium prints it as vectors.
 */
export function GazeTraceBar({ g, id }: { g: GazeBar; id: string }) {
  const W = 1000, H = 60, base = 36, down = 20, pad = 24;
  const n = Math.max(1, g.bars.length);
  const step = (W - 2 * pad) / n;
  const stroke = Math.max(0.6, Math.min(2.5, step * 0.7));
  const yFor = (f: number) => base - f * (base - 4);
  return (
    <figure className="gaze" aria-label="Rastreamento do olhar" data-gaze={id} data-samples={g.bars.length}>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" preserveAspectRatio="none">
        <line x1={pad} y1={base} x2={W - pad} y2={base} className="axis" />
        <line x1={pad} y1={yFor(0.5)} x2={W - pad} y2={yFor(0.5)} className="grid" />
        <line x1={pad} y1={yFor(1)} x2={W - pad} y2={yFor(1)} className="grid" />
        {g.limit !== null && <line x1={pad} y1={yFor(g.limit)} x2={W - pad} y2={yFor(g.limit)} className="limit" strokeDasharray="6 4" />}
        <text x={pad - 3} y={base + 3} className="lbl" textAnchor="end">0°</text>
        <text x={pad - 3} y={yFor(0.5) + 3} className="lbl" textAnchor="end">{g.clipDeg / 2}°</text>
        <text x={pad - 3} y={yFor(1) + 3} className="lbl" textAnchor="end">{g.clipDeg}°</text>
        {g.bars.map((b, i) => {
          const x = pad + (i + 0.5) * step;
          return b.rejected
            ? <line key={i} x1={x} y1={base} x2={x} y2={base + down} className="rej" strokeWidth={stroke} />
            : <line key={i} x1={x} y1={base} x2={x} y2={yFor(b.h)} className="err" strokeWidth={stroke} />;
        })}
      </svg>
      <figcaption>{gazeCaption(g)}{g.limit !== null ? ` · limite ${Math.round(g.limit * g.clipDeg * 10) / 10}°` : ""} · ↓ apresentação descartada</figcaption>
    </figure>
  );
}
