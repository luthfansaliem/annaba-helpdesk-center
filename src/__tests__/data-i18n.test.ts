import { readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { setLang, td } from "@/i18n";
import id from "@/i18n/id";
import { eventText } from "@/components/tickets/event-text";

afterEach(() => setLang("en", false));

describe("td(): text that comes from data", () => {
  it("translates the starter catalog and leaves what an administrator typed", () => {
    setLang("id", false);
    expect(td("Pool car booking")).toBe("Pemesanan mobil operasional");
    expect(td("Where is the problem?")).toBe("Di mana masalahnya?");
    expect(td("Genset 500 kVA site B")).toBe("Genset 500 kVA site B");
    expect(td(null)).toBeNull(); expect(td("")).toBe("");
    setLang("en", false); expect(td("Pool car booking")).toBe("Pool car booking");
  });

  it("has an Indonesian entry for every name, question and choice of the API's starter catalog", () => {
    const src = readFileSync(join(process.cwd(), "api/src/AnnabaDigital.Application/Helpdesk/CatalogTemplates.cs"), "utf8");
    const body = src.slice(src.indexOf("public static readonly IReadOnlyList<CategoryTemplate> All"), src.indexOf("public static async Task SeedAsync"));
    const literals = [...body.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]);
    // keys, icons and stored choice values are lower-case machine words; everything a person reads starts with a capital
    const shown = new Set(literals.filter((s) => /^[A-Z]/.test(s)));
    const helper = src.match(/Photos\(string key, string label, int max = 3\) => new\([^"]*"([^"]+)"/)?.[1];
    if (helper) shown.add(helper);
    expect(shown.size).toBeGreaterThan(100);
    expect([...shown].filter((s) => !id[s])).toEqual([]);
  });
});

describe("eventText(): system activity lines", () => {
  it("shows English unchanged in English", () => {
    expect(eventText("Assigned to Agus (Facilities) by rule “AC”.")).toBe("Assigned to Agus (Facilities) by rule “AC”.");
  });

  it("rebuilds known sentences in Indonesian with their values", () => {
    setLang("id", false);
    expect(eventText("Assigned to Agus Setiawan (Facilities Engineering) by rule “Facilities → Engineering”."))
      .toBe("Ditugaskan ke Agus Setiawan (Facilities Engineering) oleh aturan “Facilities → Engineering”.");
    expect(eventText("Assigned to Agus Setiawan (Facilities Engineering).")).toBe("Ditugaskan ke Agus Setiawan (Facilities Engineering).");
    expect(eventText("Filed on behalf of Dimas Prakoso.")).toBe("Diajukan atas nama Dimas Prakoso.");
    expect(eventText("Attached alarm-ac-presisi.png (672 B).")).toBe("Melampirkan alarm-ac-presisi.png (672 B).");
    expect(eventText("Work order GA-2026-000001-W1 created: Ganti filter (given to Agus).")).toBe("Perintah kerja GA-2026-000001-W1 dibuat: Ganti filter (diberikan ke Agus).");
    expect(eventText("Work order GA-2026-000001-W1 done: Sudah normal")).toBe("Perintah kerja GA-2026-000001-W1 selesai: Sudah normal");
    expect(eventText("SLA at risk: 75% of the time allowed has been used.")).toBe("SLA berisiko: 75% waktu yang tersedia sudah terpakai.");
    expect(eventText("Approved. The request is released to the team.")).toBe("Disetujui. Permintaan diteruskan ke tim.");
    expect(eventText("fixed: Kompresor diganti")).toMatch(/: Kompresor diganti$/);
  });

  it("translates each sentence of an edit and the priority names inside it", () => {
    setLang("id", false);
    expect(eventText("Priority changed from medium to urgent. Asset set to AC-SRV-01 AC Presisi – Ruang Server."))
      .toBe("Prioritas diubah dari sedang menjadi mendesak. Aset diisi AC-SRV-01 AC Presisi – Ruang Server.");
  });

  it("leaves what a person wrote as written", () => {
    setLang("id", false);
    expect(eventText("Kompresor unit 2 trip. Kipas portable dipasang sementara.")).toBe("Kompresor unit 2 trip. Kipas portable dipasang sementara.");
    expect(eventText(null)).toBe("");
  });
});
