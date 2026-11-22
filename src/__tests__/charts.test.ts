import { afterEach, describe, expect, it } from "vitest";
import { niceScale } from "@/components/charts/line-chart";
import { fmtDay } from "@/lib/format";
import { setLang } from "@/i18n";

afterEach(() => setLang("en", false));

describe("niceScale(): round axes for counts", () => {
  it("uses round steps instead of quarters of an odd maximum", () => {
    expect(niceScale(23)).toEqual({ max: 30, ticks: [0, 10, 20, 30] });
    expect(niceScale(25)).toEqual({ max: 30, ticks: [0, 10, 20, 30] });
    expect(niceScale(8)).toEqual({ max: 8, ticks: [0, 2, 4, 6, 8] });
    expect(niceScale(4)).toEqual({ max: 4, ticks: [0, 1, 2, 3, 4] });
    expect(niceScale(0).max).toBeGreaterThan(0);
  });
  it("always covers the peak with whole-number ticks", () => {
    for (const peak of [1, 3, 7, 11, 19, 37, 99, 101, 640]) {
      const s = niceScale(peak);
      expect(s.max).toBeGreaterThanOrEqual(peak);
      expect(s.ticks.every(Number.isInteger)).toBe(true);
      expect(s.ticks.at(-1)).toBe(s.max);
    }
  });
});

describe("fmtDay(): axis day labels", () => {
  it("reads as a day and month name, in the reader's language", () => {
    expect(fmtDay("2026-10-24")).toMatch(/24 Oct/);
    setLang("id", false);
    expect(fmtDay("2026-10-24")).toMatch(/24 Okt/);
  });
});
