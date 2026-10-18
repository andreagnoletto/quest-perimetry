// pt-BR formatting helpers for the doctor UI and the report.

/** pt-BR number; values that round to zero print without a sign ("0", never "-0"), as on the HFA printout. */
export const fmt = (v: number | null | undefined, digits = 0) =>
  v === null || v === undefined || !Number.isFinite(v) ? "—" : (Number(v.toFixed(digits)) + 0).toLocaleString("pt-BR", { minimumFractionDigits: digits, maximumFractionDigits: digits });

export const pct = (v: number | null | undefined, digits = 0) => (v === null || v === undefined ? "—" : fmt(v * 100, digits) + "%");

/** "p < 0,5%" style label; empty when not significant. */
export function pLabel(p: number | null | undefined): string {
  if (p === null || p === undefined || p >= 1) return "";
  return "p < " + fmt(p * 100, p < 0.01 ? 1 : 0) + "%";
}

export function duration(ms: number): string {
  const s = Math.round(ms / 1000);
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}

export const dateBr = (d: Date | string) => new Date(d).toLocaleDateString("pt-BR", { timeZone: "America/Sao_Paulo" });
export const dateTimeBr = (d: Date | string) =>
  new Date(d).toLocaleString("pt-BR", { timeZone: "America/Sao_Paulo", dateStyle: "short", timeStyle: "short" });

export const eyeName = (e: string) => (e === "OD" ? "Olho direito (OD)" : e === "OS" ? "Olho esquerdo (OS)" : e);

export const strategyName: Record<string, string> = {
  Zest: "ZEST",
  ZestFast: "ZEST rápido",
  FullThreshold: "Full Threshold 4-2",
  FourTwo: "Escada 4-2",
  Screening: "Rastreio",
};

export const fixationName: Record<string, string> = {
  CentralHeijlKrakau: "Heijl-Krakau (mancha cega)",
  CentralEyeTracking: "Eye-tracking",
  CentralUnmonitored: "Não monitorada",
  Oculokinetic: "Oculocinética (modo B)",
};

/** Fixation modes an exam order may ask for (core FixationMode names), in menu order. */
export const fixationModes = ["CentralHeijlKrakau", "CentralEyeTracking", "Oculokinetic", "CentralUnmonitored"] as const;

export const responseModeName: Record<string, string> = {
  YesNo: "Sim/não (botão)",
  ForcedChoice: "Escolha forçada (experimental)",
};

export const responseModes = ["YesNo", "ForcedChoice"] as const;

export const gridName: Record<string, string> = { "24-2": "24-2", "30-2": "30-2", "10-2": "10-2", "24-2C": "24-2C" };
