import { dateBr, fmt } from "@/lib/format";
import type { Trend } from "@/lib/progression";

// Index against time with the OLS line from lib/progression (descriptive; no event analysis yet).

export function TrendChart({ points, trend, unit, title, yMin, yMax }: {
  points: { date: Date; value: number | null }[];
  trend: Trend;
  unit: string;
  title: string;
  yMin: number;
  yMax: number;
}) {
  const pts = points.filter((p) => p.value !== null) as { date: Date; value: number }[];
  if (pts.length === 0) return null;
  if (pts.length < 2) return <p className="muted small"><b>{title}</b>: a tendência aparece a partir do segundo exame.</p>;
  const W = 520, H = 180, L = 44, R = 10, T = 12, B = 28;
  const t0 = Math.min(...pts.map((p) => p.date.getTime()));
  const t1 = Math.max(...pts.map((p) => p.date.getTime()), t0 + 30 * 86400_000);
  const lo = Math.min(yMin, ...pts.map((p) => p.value)), hi = Math.max(yMax, ...pts.map((p) => p.value));
  const x = (t: number) => L + ((t - t0) / (t1 - t0)) * (W - L - R);
  const y = (v: number) => T + ((hi - v) / (hi - lo || 1)) * (H - T - B);
  const yearMs = 365.2425 * 86400_000;
  const ticks = [lo, (lo + hi) / 2, hi];
  return (
    <figure className="trend card">
      <figcaption><b>{title}</b>{" "}
        {trend.slopePerYear !== null
          ? <span className="muted">tendência {fmt(trend.slopePerYear, 2)} {unit}/ano{trend.slopeSe !== null ? ` ± ${fmt(trend.slopeSe, 2)}` : ""}{trend.p !== null ? ` (p = ${fmt(trend.p, 3)})` : ""} · {trend.n} exames em {fmt(trend.spanYears, 1)} anos</span>
          : <span className="muted">{trend.n} exame(s): tendência precisa de pelo menos 2 datas diferentes</span>}
      </figcaption>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={title}>
        {ticks.map((v) => (
          <g key={v}>
            <line x1={L} x2={W - R} y1={y(v)} y2={y(v)} stroke="#ddd" />
            <text x={L - 6} y={y(v)} fontSize={11} textAnchor="end" dominantBaseline="central">{fmt(v, 0)}</text>
          </g>
        ))}
        <text x={L} y={H - 8} fontSize={11}>{dateBr(new Date(t0))}</text>
        <text x={W - R} y={H - 8} fontSize={11} textAnchor="end">{dateBr(new Date(t1))}</text>
        {trend.slopePerYear !== null && trend.intercept !== null && (
          <line x1={x(t0)} y1={y(trend.intercept)} x2={x(t1)} y2={y(trend.intercept + trend.slopePerYear * ((t1 - t0) / yearMs))} stroke="#1f5fa8" strokeDasharray="4 3" />
        )}
        {pts.map((p, i) => <circle key={i} cx={x(p.date.getTime())} cy={y(p.value)} r={3.5} fill="#000" />)}
      </svg>
    </figure>
  );
}
