import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { authFile } from "./helpers";

test.use({ storageState: authFile("admin") });

test.describe("service catalog and form builder (CAT-001/002/003)", () => {
  test("the starter catalog is installed and published", async ({ page }) => {
    await page.goto("/setup/catalog");
    await expect(page.getByRole("heading", { name: "Service Catalog" })).toBeVisible();
    const row = page.getByRole("row", { name: /Meeting room booking/ });
    await expect(row).toBeVisible();
    await expect(row.getByText("Published")).toBeVisible();

    await page.getByRole("tab", { name: "Categories" }).click();
    await expect(page.getByRole("row", { name: /Facilities & Maintenance/ })).toBeVisible();
  });

  test("a service can be created as a draft, given a form, and published", async ({ page }) => {
    const name = `E2E service ${Date.now()}`;
    await page.goto("/setup/catalog");
    await page.getByRole("button", { name: "New service" }).click();
    const dlg = page.getByRole("dialog", { name: "New service" });
    await dlg.getByLabel("Name").fill(name);
    await dlg.getByLabel("Category").selectOption({ index: 1 });
    await dlg.getByRole("button", { name: "Save" }).click();
    const row = page.getByRole("row", { name: new RegExp(name) });
    await expect(row.getByText("Draft")).toBeVisible();

    await row.getByRole("link", { name: /^Form / }).click();
    await expect(page.getByRole("heading", { name })).toBeVisible();
    await expect(page.getByText("This service has no form yet.")).toBeVisible();

    await page.getByRole("button", { name: "Add question" }).click();
    const q = page.getByRole("dialog", { name: "Add question" });
    await q.getByRole("textbox", { name: "Question", exact: true }).fill("Which floor?");
    await expect(q.getByLabel("Key")).toHaveValue("which_floor"); // the key follows the label until edited
    await q.getByRole("button", { name: "Done" }).click();
    await expect(page.getByText("1. Which floor?")).toBeVisible();

    await page.getByLabel("What changed? (optional)").fill("first draft");
    await page.getByRole("button", { name: "Save new version" }).click();
    await expect(page.getByText(/Form version 1 is in force/)).toBeVisible();
    await expect(page.getByText("In force", { exact: true })).toBeVisible();

    await page.getByRole("link", { name: "Service Catalog" }).first().click();
    await page.getByRole("row", { name: new RegExp(name) }).getByRole("button", { name: `Publish ${name}` }).click();
    await expect(page.getByRole("row", { name: new RegExp(name) }).getByText("Published")).toBeVisible();
  });

  test("the preview behaves like the real form: conditional questions appear only when their condition is met", async ({ page }) => {
    await page.goto("/setup/catalog");
    await page.getByRole("link", { name: "Form Meeting room booking" }).click();
    await expect(page.getByRole("heading", { name: "Meeting room booking" })).toBeVisible();

    const catering = page.getByRole("combobox", { name: "Do you need catering?", exact: true });
    const details = page.getByRole("textbox", { name: "What catering?", exact: true });
    await expect(catering).toBeVisible();
    await expect(details).toHaveCount(0);
    await catering.selectOption("yes");
    await expect(details).toBeVisible();
    await catering.selectOption("no");
    await expect(details).toHaveCount(0);
  });

  test("editing creates a new version, an unchanged form cannot be saved, and an old version can be loaded back as a draft", async ({ page }) => {
    await page.goto("/setup/catalog");
    await page.getByRole("link", { name: "Form Pantry supplies" }).click();
    await expect(page.getByRole("heading", { name: "Pantry supplies" })).toBeVisible();
    const save = page.getByRole("button", { name: "Save new version" });
    await expect(save).toBeDisabled(); // nothing changed yet

    await page.getByRole("button", { name: "Add question" }).click();
    const q = page.getByRole("dialog", { name: "Add question" });
    await q.getByRole("textbox", { name: "Question", exact: true }).fill("Urgent?");
    await q.getByLabel("Type").selectOption("yes_no");
    await q.getByRole("button", { name: "Done" }).click();
    await expect(save).toBeEnabled();
    await save.click();
    await expect(page.getByText(/Form version 2 is in force/)).toBeVisible();

    // history keeps v1; loading it back makes a draft that differs from what is in force
    await page.getByRole("button", { name: "Load into draft v1" }).click();
    await expect(page.getByText("2 in this draft")).toBeVisible(); // the starter form had two questions; version 2 has three
    await expect(save).toBeEnabled();
    await page.getByRole("button", { name: "Discard changes" }).click();
    await expect(save).toBeDisabled();
  });

  test("the form builder and its question drawer have no serious accessibility violations", async ({ page }) => {
    const scan = async (name: string) => {
      const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
      const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
      expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
    };
    await page.goto("/setup/catalog");
    await page.getByRole("link", { name: "Form Meeting room booking" }).click();
    await expect(page.getByRole("heading", { name: "Meeting room booking" })).toBeVisible();
    await scan("builder");
    await page.getByRole("button", { name: "Edit What catering?" }).click(); // a conditional question: shows the condition controls too
    await expect(page.getByRole("dialog", { name: "Edit question" })).toBeVisible();
    await scan("builder with drawer");
  });

  test("a broken form is refused with a message that names the question", async ({ page }) => {
    await page.goto("/setup/catalog");
    await page.getByRole("link", { name: "Form Pantry supplies" }).click();
    await page.getByRole("button", { name: "Add question" }).click();
    const q = page.getByRole("dialog", { name: "Add question" });
    await q.getByRole("textbox", { name: "Question", exact: true }).fill("Dup");
    await q.getByLabel("Key").fill("location"); // already used by the starter form
    await q.getByRole("button", { name: "Done" }).click();
    await expect(q.getByText("Another question already uses this key.")).toBeVisible(); // caught before it reaches the server
    await q.getByRole("button", { name: "Cancel" }).click();
  });
});
