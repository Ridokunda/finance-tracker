import { test, expect } from "@playwright/test";
import type { Page } from "@playwright/test";

test.use({ timezoneId: "America/Los_Angeles" });

async function dashboard(page: Page, now = "2026-10-21T22:30:00Z") {
  // Johannesburg is already on 22 October while UTC and the browser are on 21 October.
  await page.clock.install({ time: new Date(now) });
  await page.addInitScript(() => {
    localStorage.setItem("token", "cash-flow-browser-test");
    localStorage.setItem("month", "2026-08");
  });
  await page.route("**/api/budget/analytics?*", route => route.fulfill({ json: {
    year: 2026, month: 8, hasBudget: true, budget: 2000, spent: 250,
    remaining: 1750, status: "ok", needsReconfirmation: false, income: 900,
    netCashFlow: 650, categories: [], trends: [],
  } }));
  await page.route(/\/api\/transaction\?/, route => route.fulfill({ json: [] }));
}

test("cash flow presets and custom dates update independently of the overview month", async ({ page }) => {
  await dashboard(page);
  const ranges: string[] = [];
  const totals: Record<string, number> = {
    "2026-10-01/2026-10-31": 850.25,
    "2026-09-01/2026-09-30": -250,
    "2026-10-18/2026-10-22": 75,
    "2026-09-30/2026-10-01": 123.45,
    "2026-10-22/2026-10-22": 0,
  };
  await page.route("**/api/transaction/net-cash-flow?*", route => {
    const query = new URL(route.request().url()).searchParams;
    const range = `${query.get("from")}/${query.get("to")}`;
    ranges.push(range);
    return route.fulfill({ json: { netCashFlow: totals[range] } });
  });
  await page.goto("/");
  const tile = page.getByRole("region", { name: "Net cash flow", exact: true });
  const value = tile.locator(".card-value");
  const period = tile.getByLabel("Net cash flow period");
  await expect(value).toContainText(/850[.,]25/);
  await expect(period).toHaveValue("this-month");
  await expect(page.getByLabel("Month", { exact: true })).toHaveValue("2026-08");

  await period.selectOption("last-month");
  await expect(value).toContainText(/250[.,]00/);
  await expect(value).toContainText("-");
  await period.selectOption("last-five-days");
  await expect(value).toContainText(/75[.,]00/);
  await expect(tile).toContainText("18 Oct 2026 to 22 Oct 2026");

  await period.selectOption("custom");
  await tile.getByLabel("Start date").fill("2026-09-30");
  await tile.getByLabel("End date").fill("2026-10-01");
  await tile.getByRole("button", { name: "Apply period" }).click();
  await expect(value).toContainText(/123[.,]45/);
  const count = ranges.length;
  await tile.getByLabel("Start date").fill("2026-10-02");
  await tile.getByRole("button", { name: "Apply period" }).click();
  await expect(tile.getByRole("alert")).toContainText("start date on or before");
  expect(ranges).toHaveLength(count);
  await expect(value).toContainText(/123[.,]45/);

  await tile.getByLabel("Start date").fill("2026-10-22");
  await tile.getByLabel("End date").fill("2026-10-22");
  await tile.getByRole("button", { name: "Apply period" }).click();
  await expect(value).toContainText(/R\s*0[.,]00/);
  await page.getByLabel("Month", { exact: true }).fill("2026-07");
  await expect(period).toHaveValue("custom");
  await expect(value).toContainText(/R\s*0[.,]00/);
  expect([...new Set(ranges)]).toEqual(Object.keys(totals));

  await page.setViewportSize({ width: 320, height: 800 });
  await tile.scrollIntoViewIfNeeded();
  await expect(tile.getByRole("button", { name: "Apply period" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: "test-results/cash-flow-mobile.png", fullPage: true });
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.screenshot({ path: "test-results/cash-flow-desktop.png", fullPage: true });
});

for (const example of [
  { now: "2026-01-02T12:00:00Z", previous: "2025-12-01/2025-12-31", fiveDays: "2025-12-29/2026-01-02" },
  { now: "2024-03-02T12:00:00Z", previous: "2024-02-01/2024-02-29", fiveDays: "2024-02-27/2024-03-02" },
]) {
  test(`cash flow handles month and year boundaries on ${example.now}`, async ({ page }) => {
    await dashboard(page, example.now);
    const ranges: string[] = [];
    await page.route("**/api/transaction/net-cash-flow?*", route => {
      const query = new URL(route.request().url()).searchParams;
      ranges.push(`${query.get("from")}/${query.get("to")}`);
      return route.fulfill({ json: { netCashFlow: 10 } });
    });
    await page.goto("/");
    const tile = page.getByRole("region", { name: "Net cash flow", exact: true });
    await expect(tile.locator(".card-value")).toContainText(/10[.,]00/);
    await tile.getByLabel("Net cash flow period").selectOption("last-month");
    await expect.poll(() => ranges.at(-1)).toBe(example.previous);
    await tile.getByLabel("Net cash flow period").selectOption("last-five-days");
    await expect.poll(() => ranges.at(-1)).toBe(example.fiveDays);
  });
}

test("cash flow hides failed totals and retries the selected period", async ({ page }) => {
  await dashboard(page);
  let offline = true;
  await page.route("**/api/transaction/net-cash-flow?*", route => {
    return offline
      ? route.fulfill({ status: 503, body: "Unavailable" })
      : route.fulfill({ json: { netCashFlow: 42 } });
  });
  await page.goto("/");
  const tile = page.getByRole("region", { name: "Net cash flow", exact: true });
  await expect(tile.getByRole("alert")).toContainText("Cash flow could not be loaded.");
  await expect(tile.locator(".card-value")).toHaveText("—");
  offline = false;
  await tile.getByRole("button", { name: "Retry" }).click();
  await expect(tile.locator(".card-value")).toContainText(/42[.,]00/);
  await expect(tile.getByRole("alert")).toHaveCount(0);
});
