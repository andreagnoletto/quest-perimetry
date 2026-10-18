import type { GazeSample, Report } from "./report-types.ts";
import { fixationName, fmt, pLabel, responseModeName } from "./format.ts";

// Phase 5 report lines, kept as pure functions so they can be unit-tested without rendering. Every number
// comes from the /core report; nothing clinical is computed here.

/** Phase 6 footer text: which normative the analysis used ("" for an exam without analysis). */
export function normativeLine(report: Report): string {
  if (!report.analysis) return "";
  const n = report.analyzedWith.normative;
  if (n && n.source === "cohort") {
    const age = n.ageMin !== null && n.ageMax !== null ? `, ${Math.round(n.ageMin)}–${Math.round(n.ageMax)} anos` : "";
    return `normativo próprio ${n.id} (${n.populationEyes} olhos${age}${n.status === "PROVISIONAL" ? ", provisório" : ""})`;
  }
  return "normativo provisório sintético";
}

/** "Limiar foveal" value as HFA prints "Fovea: 35 dB" / "Fovea: OFF"; the p-mark is the foveal total deviation. */
export function fovealLine(report: Report, floor: number): { value: string; p: string; tested: boolean } {
  const f = report.exam.Foveal;
  if (!f) return { value: "não testado", p: "", tested: false };
  const value = f.CensoredLow ? `<${floor} dB` : `${fmt(f.ThresholdHfaDb)} dB`;
  return { value, p: pLabel(f.TdP), tested: true };
}

/** Fixation monitor line: mode, plus the response mode when it is not the plain yes/no button. */
export function fixationLine(report: Report): string {
  const s = report.exam.Settings;
  const parts = [fixationName[s.FixationMode] ?? s.FixationMode];
  if (s.ResponseMode && s.ResponseMode !== "YesNo") parts.push(responseModeName[s.ResponseMode] ?? s.ResponseMode);
  return parts.join(" · ");
}

/** Fixation target line: central target, 4-dot diamond for the foveal test, moving target in mode B. */
export function fixationTargetLine(report: Report): string {
  const s = report.exam.Settings;
  const bits: string[] = [s.FixationMode === "Oculokinetic" ? "Móvel (oculocinético)" : "Central"];
  if (s.FovealThreshold) bits.push("losango de 4 pontos na fóvea");
  return bits.join(" + ");
}

export interface GazeBar {
  /** One entry per traced presentation, in order; `h` is the bar height 0..1 (error clipped at `clipDeg`). */
  bars: { h: number; rejected: boolean }[];
  clipDeg: number;
  /** Rejection threshold as a fraction of clipDeg (for the dashed limit line), null when unknown. */
  limit: number | null;
  meanDeg: number | null;
  rejected: number;
  samples: number;
}

/** Data for the HFA-style gaze bar: up = error at stimulus onset, down = presentation rejected (tracking lost). */
export function gazeBar(report: Report, clipDeg = 10): GazeBar | null {
  const r = report.exam.Reliability;
  if (!r.GazeTracked) return null;
  const trace: GazeSample[] = r.GazeTrace ?? [];
  const bars = trace.map((t) => ({ h: t.ErrorDeg === null ? 0 : Math.min(1, Math.max(0, t.ErrorDeg) / clipDeg), rejected: t.Rejected }));
  const limit = r.GazeMaxErrorDeg && r.GazeMaxErrorDeg > 0 && r.GazeMaxErrorDeg < clipDeg ? r.GazeMaxErrorDeg / clipDeg : null;
  return { bars, clipDeg, limit, meanDeg: r.GazeMeanErrorDeg ?? null, rejected: r.GazeRejectedPresentations ?? trace.filter((t) => t.Rejected).length, samples: r.GazeSamples ?? trace.length };
}

export function gazeCaption(g: GazeBar): string {
  return `Rastreamento do olhar: erro médio ${g.meanDeg === null ? "—" : fmt(g.meanDeg, 1) + "°"}, ${g.rejected} ${g.rejected === 1 ? "descartada" : "descartadas"} (${g.samples} apresentações rastreadas)`;
}
