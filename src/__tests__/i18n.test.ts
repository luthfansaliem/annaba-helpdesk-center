import { afterEach, describe, expect, it } from "vitest";
import { getLang, setLang, t } from "@/i18n";
import id from "@/i18n/id";
import { fmtDate, rel } from "@/lib/format";

afterEach(() => setLang("en", false));

describe("t()", () => {
  it("returns the English text when there is no translation, in either language", () => {
    expect(t("A sentence nobody has translated yet.")).toBe("A sentence nobody has translated yet.");
    setLang("id", false);
    expect(t("A sentence nobody has translated yet.")).toBe("A sentence nobody has translated yet.");
  });
  it("translates in Indonesian and goes back", () => {
    setLang("id", false); expect(getLang()).toBe("id"); expect(t("Sign In")).toBe("Masuk");
    setLang("en", false); expect(t("Sign In")).toBe("Sign In");
  });
  it("fills {placeholders}, keeping unknown ones visible", () => {
    expect(t("Suspend {name}?", { name: "Sarah" })).toBe("Suspend Sarah?");
    setLang("id", false); expect(t("Suspend {name}?", { name: "Sarah" })).toBe("Tangguhkan Sarah?");
    expect(t("Hello {who}", {})).toBe("Hello {who}");
  });
  it("keeps the same {placeholders} in every Indonesian entry as in its English key", () => {
    const names = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(",");
    const bad = Object.entries(id).filter(([en, tr]) => tr && names(en) !== names(tr) && !/\{v\d\}/.test(en));
    expect(bad.map(([en]) => en)).toEqual([]);
  });
});

describe("formatting follows the language", () => {
  it("relative times", () => {
    const now = Date.parse("2026-11-07T10:00:00Z"); const ago = (m: number) => new Date(now - m * 60000).toISOString();
    expect(rel(null)).toBe("Never"); expect(rel(ago(0), now)).toBe("Just now"); expect(rel(ago(5), now)).toBe("5 mins ago"); expect(rel(ago(120), now)).toBe("2 hours ago");
    setLang("id", false);
    expect(rel(null)).toBe("Tidak pernah"); expect(rel(ago(0), now)).toBe("Baru saja"); expect(rel(ago(5), now)).toBe("5 menit lalu"); expect(rel(ago(120), now)).toBe("2 jam lalu"); expect(rel(ago(60 * 24 * 3), now)).toBe("3 hari lalu");
  });
  it("dates use the language's month names", () => {
    expect(fmtDate("2026-05-17T00:00:00Z")).toMatch(/May/); setLang("id", false); expect(fmtDate("2026-05-17T00:00:00Z")).toMatch(/Mei/);
  });
});
