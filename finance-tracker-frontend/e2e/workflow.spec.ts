import { test, expect } from "@playwright/test";
test("login, review during classifier outage, confirm, budget and dashboard drill-down", async ({
  page,
  request,
}) => {
  const username = "browser-" + Date.now();
  const passwordHash = "Test-Rands-2026!";
  expect(
    (
      await request.post("/api/auth/register", {
        data: { username, passwordHash },
      })
    ).ok(),
  ).toBeTruthy();
  await page.goto("/login");
  await page.getByLabel("Username", { exact: true }).fill(username);
  await page.getByLabel("Password", { exact: true }).fill(passwordHash);
  await page.getByRole("button", { name: "Login", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Your monthly overview" }),
  ).toBeVisible();
  await page.getByLabel("Month", { exact: true }).fill("2026-09");
  await page.getByRole("link", { name: "Budgets", exact: true }).click();
  await page.getByLabel("Overall monthly limit (ZAR)").fill("5000");
  await page.getByRole("button", { name: "Save budget", exact: true }).click();
  await expect(page.getByText("Budget saved.", { exact: true })).toBeVisible();
  await page.getByRole("link", { name: "Transactions", exact: true }).click();
  await page
    .getByRole("button", { name: "Import statement", exact: true })
    .click();
  await page
    .getByLabel("Statement file")
    .setInputFiles({
      name: "review.csv",
      mimeType: "text/csv",
      buffer: Buffer.from(
        'Date,Description,Amount\n2026-09-01,"Checkers, Cape Town",-1200.00\n2026-09-01,"Checkers, Cape Town",-1200.00\n',
      ),
    });
  await page
    .getByRole("button", { name: "Preview statement", exact: true })
    .click();
  const review = page.getByRole("dialog");
  await expect(review).toBeVisible();
  await expect(
    review.getByText(/Classification service unavailable/),
  ).toBeVisible();
  await expect(review.getByLabel("Include row 2")).toBeChecked();
  await expect(review.getByLabel("Include row 3")).not.toBeChecked();
  await review
    .getByLabel("Category row 2", { exact: true })
    .selectOption("Groceries");
  const auth = await page.evaluate(() => localStorage.getItem("token"));
  const headers = { Authorization: "Bearer " + auth };
  expect(
    await (await request.get("/api/transaction", { headers })).json(),
  ).toEqual([]);
  await review.getByRole("button", { name: "Confirm 1 transactions" }).click();
  await expect(
    page.getByText("Imported 1 transactions. Budget limits are unchanged."),
  ).toBeVisible();
  const summary = await (
    await request.get("/api/budget/analytics?year=2026&month=9", { headers })
  ).json();
  expect(summary.budget).toBe(5000);
  expect(summary.spent).toBe(1200);
  expect(summary.remaining).toBe(3800);
  await page.getByRole("link", { name: "Budgets", exact: true }).click();
  await page
    .getByRole("button", { name: "Add category limit", exact: true })
    .click();
  await page.getByLabel("Groceries limit (ZAR)").fill("1500");
  await page.getByRole("button", { name: "Save budget", exact: true }).click();
  await expect(page.getByText("Budget saved.", { exact: true })).toBeVisible();
  await page.getByRole("link", { name: "Dashboard", exact: true }).click();
  await page.locator(".legend").getByRole("link", { name: "Groceries", exact: true }).click();
  await expect(page.getByLabel("Category", { exact: true })).toHaveValue(
    "Groceries",
  );
  await expect(
    page.getByRole("cell", { name: "Checkers, Cape Town", exact: true }),
  ).toBeVisible();
  // Verify user isolation through actual authenticated HTTP endpoints.
  const other = { username: username + "-other", passwordHash };
  await request.post("/api/auth/register", { data: other });
  const otherAuth = await (
    await request.post("/api/auth/login", { data: other })
  ).json();
  const otherHeaders = { Authorization: "Bearer " + otherAuth.token };
  expect(
    await (
      await request.get("/api/transaction", { headers: otherHeaders })
    ).json(),
  ).toEqual([]);
  expect(
    (
      await request.get("/api/budget/analytics?year=2026&month=9", {
        headers: otherHeaders,
      })
    ).ok(),
  ).toBeTruthy();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("link", { name: "Budgets", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Add transaction", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
