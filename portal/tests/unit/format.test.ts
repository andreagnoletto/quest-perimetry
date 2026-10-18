import { describe, expect, it } from "vitest";
import { fmt, pLabel } from "@/lib/format";

describe("format", () => {
  it("prints pt-BR numbers without negative zero", () => {
    expect(fmt(-0.3)).toBe("0");
    expect(fmt(-0.6)).toBe("-1");
    expect(fmt(-3.537, 2)).toBe("-3,54");
    expect(fmt(null)).toBe("—");
    expect(pLabel(0.005)).toBe("p < 0,5%");
    expect(pLabel(0.02)).toBe("p < 2%");
    expect(pLabel(1)).toBe("");
  });
});
