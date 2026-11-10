import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("requests and tickets (TKT-001/002/004/008)", () => {
  test("a request travels from the requester to an agent and back to confirmation", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const subject = `E2E courier ${Date.now()}`;

    // --- the requester finds a service and fills its form
    await member.goto("/requests/new");
    await member.getByLabel("Search services").fill("courier");
    await member.getByRole("link", { name: /Courier & package dispatch/ }).click();
    await expect(member.getByRole("heading", { name: "Courier & package dispatch" })).toBeVisible();
    await axe(member, "request form");

    await member.getByRole("button", { name: "Send request" }).click();
    await expect(member.getByText("This is required.").first()).toBeVisible();       // the server names what is missing, per question

    await member.getByLabel("Subject").fill(subject);
    await member.getByRole("textbox", { name: "Delivery address" }).fill("Jl. Sudirman 1, Jakarta");
    await member.getByRole("textbox", { name: "Recipient" }).fill("Budi");
    await member.getByRole("combobox", { name: "How fast?" }).selectOption("same_day");
    await member.getByRole("textbox", { name: "What are you sending?" }).fill("Signed contract, 12 pages");
    await member.getByRole("button", { name: "Send request" }).click();

    await expect(member.getByText("Your request has been sent")).toBeVisible();
    const heading = await member.getByRole("heading", { level: 1 }).innerText();
    const number = heading.split(" · ")[0];
    expect(number).toMatch(/^GA-\d{4}-\d{6}$/);
    await expect(member.getByText("Submitted", { exact: true }).first()).toBeVisible();
    await expect(member.getByText("Signed contract, 12 pages")).toBeVisible();
    await expect(member.getByRole("button", { name: "Cancel request" })).toBeVisible();   // allowed before work starts
    await axe(member, "ticket detail (requester)");

    await member.goto("/requests");
    await expect(member.getByRole("link", { name: subject })).toBeVisible();

    // --- a supervisor assigns it, works it, leaves an internal note and resolves it
    await admin.goto("/tickets");
    await admin.getByLabel("Search", { exact: true }).fill(number);
    await admin.getByRole("link", { name: number }).first().click();
    await expect(admin.getByRole("heading", { level: 1 })).toContainText(number);

    await admin.getByRole("button", { name: "Assign", exact: true }).click();
    const assign = admin.getByRole("dialog", { name: "Assign ticket" });
    await assign.getByLabel("Person").selectOption({ label: "Sarah Wijaya" });
    await assign.getByRole("button", { name: "Assign", exact: true }).click();
    await expect(admin.getByText("Ticket assigned.")).toBeVisible();
    await expect(admin.getByText("Assigned", { exact: true }).first()).toBeVisible();

    await admin.getByRole("button", { name: "Start work" }).click();
    await admin.getByRole("dialog", { name: "Start work" }).getByRole("button", { name: "Start work" }).click();
    await expect(admin.getByText("In progress", { exact: true }).first()).toBeVisible();

    await admin.getByLabel("Add a comment").fill("Courier booked for 14:00 — internal only");
    await admin.getByLabel("Internal note (requester will not see it)").check();
    await admin.getByRole("button", { name: "Add internal note" }).click();
    await expect(admin.getByText("Note added.")).toBeVisible();                       // wait for the note to be saved (the textarea also holds that text until then)
    await expect(admin.getByLabel("Add a comment")).toHaveValue("");
    await expect(admin.getByRole("listitem").filter({ hasText: "Courier booked for 14:00" })).toBeVisible();
    await admin.getByLabel("Add a comment").fill("Your parcel is on its way.");
    await admin.getByRole("button", { name: "Send comment" }).click();
    await expect(admin.getByText("Comment added.")).toBeVisible();
    await expect(admin.getByRole("listitem").filter({ hasText: "Your parcel is on its way." })).toBeVisible();
    await axe(admin, "ticket detail (agent)");

    await admin.getByRole("button", { name: "Resolve", exact: true }).click();
    const resolve = admin.getByRole("dialog", { name: "Resolve" });
    await resolve.getByRole("button", { name: "Resolve", exact: true }).click();
    await expect(resolve.getByText("Describe what was done")).toBeVisible();           // a resolution note is required
    await resolve.getByRole("textbox", { name: "What was done?" }).fill("Delivered and signed for by Budi.");
    await resolve.getByRole("button", { name: "Resolve", exact: true }).click();
    await expect(admin.getByText("Resolved", { exact: true }).first()).toBeVisible();

    // --- the requester sees the public comment but never the internal note, and confirms
    await member.goto("/requests");
    await member.getByRole("link", { name: number }).click();
    await expect(member.getByText("Your parcel is on its way.")).toBeVisible();
    await expect(member.getByText("Courier booked for 14:00")).toHaveCount(0);
    await expect(member.getByText("Delivered and signed for by Budi.").first()).toBeVisible();   // shown in the Resolution panel and on the timeline
    await member.getByRole("button", { name: "Confirm it is solved" }).click();
    await member.getByRole("dialog", { name: "Confirm it is solved" }).getByRole("button", { name: "Confirm it is solved" }).click();
    await expect(member.getByText("Closed", { exact: true }).first()).toBeVisible();
    await expect(member.getByRole("button", { name: "Reopen" })).toBeVisible();        // still reopenable for a while
  });

  test("a person cannot open someone else's ticket or the staff queue", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/tickets");
    await expect(member.getByText(/permission|access/i).first()).toBeVisible();      // the queue is for staff
    await member.goto("/tickets/00000000-0000-7000-8000-000000000000");
    await expect(member.getByText("Not found").first()).toBeVisible();
  });
});
