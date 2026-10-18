/**
 * Ordinary least squares of an index against time, for the trend panel (SPEC §7: base for progression
 * analysis by linear regression of MD and VFI; GPA-like event analysis is a later phase).
 */
export interface Trend {
  n: number;
  slopePerYear: number | null;
  slopeSe: number | null;
  intercept: number | null;
  spanYears: number;
  /** Two-sided p-value of slope = 0 (t distribution, n − 2 df); null when n < 3. */
  p: number | null;
}

export function trend(points: { date: Date; value: number | null }[]): Trend {
  const pts = points.filter((p) => p.value !== null && Number.isFinite(p.value)) as { date: Date; value: number }[];
  const n = pts.length;
  if (n < 2) return { n, slopePerYear: null, slopeSe: null, intercept: null, spanYears: 0, p: null };
  const t0 = Math.min(...pts.map((p) => p.date.getTime()));
  const xs = pts.map((p) => (p.date.getTime() - t0) / (365.2425 * 24 * 3600 * 1000));
  const ys = pts.map((p) => p.value);
  const mx = xs.reduce((a, b) => a + b, 0) / n;
  const my = ys.reduce((a, b) => a + b, 0) / n;
  let sxx = 0, sxy = 0;
  for (let i = 0; i < n; i++) { sxx += (xs[i] - mx) ** 2; sxy += (xs[i] - mx) * (ys[i] - my); }
  const span = Math.max(...xs) - Math.min(...xs);
  if (sxx === 0) return { n, slopePerYear: null, slopeSe: null, intercept: null, spanYears: span, p: null };
  const b = sxy / sxx, a = my - b * mx;
  if (n < 3) return { n, slopePerYear: b, slopeSe: null, intercept: a, spanYears: span, p: null };
  let sse = 0;
  for (let i = 0; i < n; i++) sse += (ys[i] - a - b * xs[i]) ** 2;
  const se = Math.sqrt(sse / (n - 2) / sxx);
  const p = se === 0 ? 0 : 2 * (1 - studentTCdf(Math.abs(b / se), n - 2));
  return { n, slopePerYear: b, slopeSe: se, intercept: a, spanYears: span, p };
}

/** Student t CDF via the regularized incomplete beta function (Numerical Recipes continued fraction). */
export function studentTCdf(t: number, df: number): number {
  const x = df / (df + t * t);
  const ib = incompleteBeta(x, df / 2, 0.5);
  return t >= 0 ? 1 - ib / 2 : ib / 2;
}

function incompleteBeta(x: number, a: number, b: number): number {
  if (x <= 0) return 0;
  if (x >= 1) return 1;
  const lbeta = lgamma(a + b) - lgamma(a) - lgamma(b);
  const front = Math.exp(Math.log(x) * a + Math.log(1 - x) * b + lbeta);
  if (x < (a + 1) / (a + b + 2)) return (front * betacf(x, a, b)) / a;
  return 1 - (front * betacf(1 - x, b, a)) / b;
}

function betacf(x: number, a: number, b: number): number {
  const FPMIN = 1e-300;
  let c = 1, d = 1 - ((a + b) * x) / (a + 1);
  if (Math.abs(d) < FPMIN) d = FPMIN;
  d = 1 / d;
  let h = d;
  for (let m = 1; m <= 300; m++) {
    const m2 = 2 * m;
    let aa = (m * (b - m) * x) / ((a + m2 - 1) * (a + m2));
    d = 1 + aa * d; if (Math.abs(d) < FPMIN) d = FPMIN;
    c = 1 + aa / c; if (Math.abs(c) < FPMIN) c = FPMIN;
    d = 1 / d; h *= d * c;
    aa = (-(a + m) * (a + b + m) * x) / ((a + m2) * (a + m2 + 1));
    d = 1 + aa * d; if (Math.abs(d) < FPMIN) d = FPMIN;
    c = 1 + aa / c; if (Math.abs(c) < FPMIN) c = FPMIN;
    d = 1 / d;
    const del = d * c;
    h *= del;
    if (Math.abs(del - 1) < 1e-12) break;
  }
  return h;
}

function lgamma(z: number): number {
  // Lanczos approximation (g = 7, n = 9)
  const g = 7;
  const c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
    12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
  if (z < 0.5) return Math.log(Math.PI / Math.sin(Math.PI * z)) - lgamma(1 - z);
  z -= 1;
  let x = c[0];
  for (let i = 1; i < g + 2; i++) x += c[i] / (z + i);
  const t = z + g + 0.5;
  return 0.5 * Math.log(2 * Math.PI) + (z + 0.5) * Math.log(t) - t + Math.log(x);
}
