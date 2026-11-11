import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
const pad = (n: number) => String(n).padStart(2, "0");
/** A date a week ahead in this machine's time zone (the browser runs here too). */
function weekAhead() { const d = new Date(); d.setDate(d.getDate() + 7); return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; }

test.describe("bookings (R3-1)", () => {
  test("GA adds a room, a member books it, the timeline shows it, a clash is refused and the booking is cancelled", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const mp = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const room = `Garuda ${uniq()}`; const day = weekAhead();

    // --- GA adds a room that can be booked around the clock, every day
    await page.goto("/setup/resources");
    await expect(page.getByRole("heading", { name: "Bookable resources" })).toBeVisible();
    await page.getByRole("button", { name: "New resource" }).click();
    const dlg = page.getByRole("dialog", { name: "New resource" });
    await dlg.locator("#rs-name").fill(room);
    await dlg.locator("#rs-cap").fill("6");
    await dlg.getByRole("checkbox", { name: "Projector" }).check();
    for (const d of ["Sat", "Sun"]) await dlg.getByRole("group", { name: "Days" }).getByRole("checkbox", { name: d }).check();
    await dlg.getByRole("checkbox", { name: "Around the clock" }).check();
    await axe(page, "new resource");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("button", { name: room })).toBeVisible();
    await axe(page, "bookable resources");
    const res = (await (await admin.get(`/api/resources?q=${encodeURIComponent(room)}`)).json())[0] as { id: string };

    // --- a member books it from Bookings
    await mp.goto("/bookings");
    await expect(mp.getByRole("heading", { name: "Bookings", level: 1 })).toBeVisible();
    await expect(mp.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Bookings" })).toBeVisible();
    await mp.getByRole("button", { name: "New booking" }).click();
    const bk = mp.getByRole("dialog", { name: "New booking" });
    await bk.locator("#bk-res").selectOption({ label: `Meeting room · ${room} (6)` });
    await bk.locator("#bk-date").fill(day);
    await bk.locator("#bk-from").fill("10:00"); await bk.locator("#bk-to").fill("11:00");
    await bk.getByLabel("What is it for?").fill("Sprint review");
    await bk.getByLabel("People").fill("5");
    await bk.getByRole("button", { name: "Book", exact: true }).click();
    await expect(bk).toBeHidden();
    const mine = mp.getByTestId("my-bookings").getByRole("listitem").filter({ hasText: "Sprint review" });
    await expect(mine).toContainText(room);

    // --- the day view shows it on the room's track
    await mp.locator("#bk-day").fill(day);
    await expect(mp.getByRole("button", { name: /10:00 Sprint review – 11:00/ })).toBeVisible();   // the name starts with the visible text (WCAG 2.5.3)
    await axe(mp, "bookings");

    // --- the same slot is refused, for the UI and the API alike
    const clash = await admin.post("/api/bookings", { data: { resourceId: res.id, startsAt: new Date(`${day}T10:30:00`).toISOString(), endsAt: new Date(`${day}T11:30:00`).toISOString(), title: "Clash" }, headers: await hdr(admin) });
    expect(clash.status()).toBe(409);
    expect((await clash.json()).error.message).toContain("already taken");

    // --- the member cancels it and the slot is free again
    await mine.getByRole("button", { name: "Cancel" }).click();
    const cancel = mp.getByRole("dialog", { name: "Cancel this booking?" });
    await cancel.getByRole("button", { name: "Cancel booking" }).click();
    await expect(mine).toHaveCount(0);
    const again = await admin.post("/api/bookings", { data: { resourceId: res.id, startsAt: new Date(`${day}T10:30:00`).toISOString(), endsAt: new Date(`${day}T11:30:00`).toISOString(), title: "Now free" }, headers: await hdr(admin) });
    expect(again.status()).toBe(201);
  });

  test("a room that needs approval waits for GA, and a member subscribes to their bookings in a calendar (R3-2)", async ({ browser, playwright }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const mp = await (await browser.newContext({ storageState: authFile("member") })).newPage(); const member = mp.request;
    const room = `Board room ${uniq()}`; const day = weekAhead();
    const res = await (await admin.post("/api/resources", { data: { kind: "room", name: room, capacity: 14, openDays: 127, needsApproval: true, checkInMinutes: 15 }, headers: await hdr(admin) })).json() as { id: string };
    const made = await member.post("/api/bookings", { data: { resourceId: res.id, startsAt: new Date(`${day}T14:00:00`).toISOString(), endsAt: new Date(`${day}T15:00:00`).toISOString(), title: "Quarterly review" }, headers: await hdr(member) });
    expect(made.status()).toBe(201);
    expect((await made.json()).status).toBe("pending");

    // the member sees it waiting; GA approves it from Bookings
    await mp.goto("/bookings");
    const mine = mp.getByTestId("my-bookings").getByRole("listitem").filter({ hasText: "Quarterly review" });
    await expect(mine).toContainText("Waiting for approval");
    await page.goto("/bookings");
    const pending = page.getByTestId("pending-bookings").getByRole("listitem").filter({ hasText: "Quarterly review" });
    await axe(page, "bookings with approvals");
    await pending.getByRole("button", { name: "Approve" }).click();
    await expect(pending).toHaveCount(0);
    await mp.reload();
    await expect(mine).toBeVisible();
    await expect(mine).not.toContainText("Waiting for approval");

    // the private calendar feed works without a session and stops when turned off
    await mp.getByRole("button", { name: "Calendar feed" }).click();
    const dlg = mp.getByRole("dialog", { name: "Your bookings in your calendar" });
    await dlg.getByRole("button", { name: "Make a link" }).click();
    const url = await dlg.getByLabel("Your calendar link (shown once)").inputValue();
    expect(url).toMatch(/\/api\/calendar\/[\w-]+\.ics$/);
    await axe(mp, "calendar feed");
    const anon = await playwright.request.newContext({ baseURL: WEB });
    const ics = await anon.get(new URL(url).pathname);
    expect(ics.status()).toBe(200);
    expect(await ics.text()).toContain("Quarterly review");
    await dlg.getByRole("button", { name: "Turn the link off" }).click();
    await expect(dlg.getByText("You have no calendar link yet.")).toBeVisible();
    expect((await anon.get(new URL(url).pathname)).status()).toBe(404);
  });

  test("a member takes a pool car on a trip and brings it back, and GA sees the use and its cost (R3-3)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const mp = await (await browser.newContext({ storageState: authFile("member") })).newPage(); const member = mp.request;
    const car = `Avanza ${uniq()}`;
    const res = await (await admin.post("/api/resources", { data: { kind: "vehicle", name: car, code: `B ${uniq()}`.toUpperCase(), capacity: 6, openDays: 127 }, headers: await hdr(admin) })).json() as { id: string };
    const start = new Date(Math.ceil(Date.now() / 300_000) * 300_000 + 600_000);   // a 5-minute step about 10 minutes from now: the car can be picked up
    const made = await member.post("/api/bookings", { data: { resourceId: res.id, startsAt: start.toISOString(), endsAt: new Date(start.getTime() + 4 * 3600_000).toISOString(), title: "Client visit" }, headers: await hdr(member) });
    expect(made.status()).toBe(201);

    await mp.goto("/bookings");
    await mp.getByTestId("my-bookings").getByRole("listitem").filter({ hasText: "Client visit" }).getByRole("button", { name: "Trip" }).click();
    const dlg = mp.getByRole("dialog", { name: "Trip" });
    await dlg.getByLabel("Destination").fill("Client office, Sudirman");
    await dlg.getByRole("button", { name: "Save trip" }).click();
    await expect(mp.getByText("Trip saved.")).toBeVisible();
    await dlg.locator("#tr-out").fill("1000");
    await dlg.getByRole("button", { name: "Pick up the car" }).click();
    await expect(dlg.getByText("On the road")).toBeVisible();
    await axe(mp, "trip");
    await dlg.locator("#tr-in").fill("1050");
    await dlg.getByLabel("Fuel", { exact: true }).fill("100000");
    for (const box of await dlg.getByRole("group", { name: "Return checklist" }).getByRole("checkbox").all()) await box.check();
    await dlg.getByRole("button", { name: "Return the car" }).click();
    await expect(dlg.getByTestId("trip-summary")).toContainText("50 km");

    // GA: the utilisation report lists the car with its kilometres
    await page.goto("/reports/utilisation");
    await page.getByLabel("Kind").selectOption("vehicle");
    await expect(page.getByRole("row").filter({ hasText: car })).toContainText("50");
    await axe(page, "utilisation");
  });
});
