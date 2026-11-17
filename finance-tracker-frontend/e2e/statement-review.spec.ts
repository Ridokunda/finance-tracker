import { test, expect } from "@playwright/test";
import type { Preview } from "../src/services/statement";

test("PDF review explains exclusions and requires an explicit decision for repeated purchases", async ({ page }, testInfo) => {
  const preview: Preview = {
    id: "00000000-0000-0000-0000-000000000001",
    expiresAt: "2026-11-18T12:00:00Z", confirmedAt: null, confirmedCount: 0,
    statementStart: "2026-10-17T00:00:00", statementEnd: "2026-11-16T00:00:00", columns: [],
    notices: ["Running balances reconcile from the opening balance to the final transaction. Review all rows before confirming."],
    rows: [
      { id: 1, rowNumber: 10, date: "2026-11-01", description: "Demo credit", amount: 250, category: "Income", error: null, possibleDuplicate: false, included: true },
      { id: 2, rowNumber: 12, date: "2026-11-02", description: "Demo purchase Card No. 0000 Demo Store", amount: -25, category: "Shopping", error: null, possibleDuplicate: false, included: true },
      { id: 3, rowNumber: 14, date: "2026-11-03", description: "Notific Fee Sms 18 Sms Notifications", amount: 0, category: "Uncategorized", error: "Balance-only entry has no debit or credit amount and is excluded.", possibleDuplicate: false, included: false },
      { id: 4, rowNumber: 16, date: "2026-11-02", description: "Demo purchase Card No. 0000 Demo Store", amount: -25, category: "Shopping", error: null, possibleDuplicate: true, included: false },
    ],
  };
  const confirmations: unknown[] = [];
  await page.addInitScript(() => localStorage.setItem("token", "statement-review-browser-test"));
  // Browser checks use a mocked preview; real PDF parsing and confirmation run in backend tests.
  await page.route("**/api/**", route => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/statement/preview") return route.fulfill({ json: preview });
    if (path.endsWith("/confirm")) {
      confirmations.push(route.request().postDataJSON());
      return route.fulfill({ json: { id: preview.id, count: 3 } });
    }
    return route.fulfill({ json: [] });
  });
  await page.goto("/transactions");
  await page.getByRole("button", { name: "Import statement", exact: true }).click();
  await page.getByLabel("Statement file").setInputFiles({
    name: "demo-cheque.pdf", mimeType: "application/pdf", buffer: Buffer.from("%PDF-1.7\n% UI upload fixture"),
  });
  await expect(page.getByRole("group", { name: "CSV format" })).toHaveCount(0);
  await page.getByRole("button", { name: "Preview statement", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Review statement" });
  await expect(review).toBeVisible();
  await expect(review).toContainText("Statement period: 2026-10-17");
  await expect(review).toContainText("4 statement entries · 1 excluded by validation · 1 possible duplicate.");
  await expect(review).toContainText("Running balances reconcile");
  await expect(review.getByLabel("Include row 14")).toBeDisabled();
  await expect(review.getByLabel("Include row 14")).not.toBeChecked();
  await expect(review.getByLabel("Include row 16")).not.toBeChecked();
  await expect(review.getByRole("button", { name: "Confirm 2 transactions" })).toBeEnabled();
  expect(confirmations).toEqual([]);

  await review.getByLabel("Include row 16").check();
  await expect(review.getByRole("button", { name: "Confirm 3 transactions" })).toBeDisabled();
  const repeated = review.getByRole("row").filter({ has: page.getByLabel("Include row 16") });
  await repeated.getByLabel("Keep duplicate").check();
  await review.getByLabel("Category row 12", { exact: true }).selectOption("Groceries");
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: testInfo.outputPath("cheque-review-mobile.png"), fullPage: true });
  await review.getByRole("button", { name: "Confirm 3 transactions" }).click();
  await expect(page.getByText("Imported 3 transactions. Budget limits are unchanged.")).toBeVisible();
  expect(confirmations).toEqual([{ rows: [
    { id: 1, category: "Income", included: true, keepDuplicate: false },
    { id: 2, category: "Groceries", included: true, keepDuplicate: false },
    { id: 3, category: "Uncategorized", included: false, keepDuplicate: false },
    { id: 4, category: "Shopping", included: true, keepDuplicate: true },
  ] }]);
});
