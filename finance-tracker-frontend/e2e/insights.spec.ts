import { test, expect } from "@playwright/test";

const SAMPLE_ANALYTICS = {
  year: 2026,
  month: 9,
  hasBudget: true,
  budget: 18000,
  spent: 12354,
  remaining: 5646,
  status: "ok",
  needsReconfirmation: false,
  income: 26500,
  netCashFlow: 14146,
  savingsRate: 53.4,
  previousMonthSavingsRate: 49.2,
  comparisons: {
    monthOverMonth: { previous: 12249, change: 105, changePercent: 0.9 },
    yearOverYear: { previous: 11200, change: 1154, changePercent: 10.3 },
  },
  categories: [
    {
      category: "Groceries",
      limit: 4000,
      spent: 2800,
      remaining: 1600,
      progress: 63.6,
      status: "Within budget",
      rolloverEnabled: true,
      rolloverAmount: 400,
      effectiveLimit: 4400,
      previousMonthSpent: 2500,
      monthOverMonthChange: 300,
      monthOverMonthPercent: 12.0,
      previousYearSpent: 3200,
      yearOverYearChange: -400,
      yearOverYearPercent: -12.5,
    },
    {
      category: "Rent",
      limit: 8000,
      spent: 8000,
      remaining: 0,
      progress: 100,
      status: "Within budget",
      rolloverEnabled: false,
      rolloverAmount: 0,
      effectiveLimit: 8000,
      previousMonthSpent: 8000,
      monthOverMonthChange: 0,
      monthOverMonthPercent: 0.0,
      previousYearSpent: 7500,
      yearOverYearChange: 500,
      yearOverYearPercent: 6.7,
    },
    {
      category: "Utilities",
      limit: 1600,
      spent: 1554,
      remaining: 46,
      progress: 97.1,
      status: "Within budget",
      rolloverEnabled: false,
      rolloverAmount: 0,
      effectiveLimit: 1600,
      previousMonthSpent: 1749,
      monthOverMonthChange: -195,
      monthOverMonthPercent: -11.1,
      previousYearSpent: 500,
      yearOverYearChange: 1054,
      yearOverYearPercent: 210.8,
    },
  ],
  trends: [
    { month: "2026-04", income: 26500, expenses: 11500, netCashFlow: 15000, savingsRate: 56.6 },
    { month: "2026-05", income: 26500, expenses: 12100, netCashFlow: 14400, savingsRate: 54.3 },
    { month: "2026-06", income: 10000, expenses: 11800, netCashFlow: -1800, savingsRate: -18.0 },
    { month: "2026-07", income: 26500, expenses: 12000, netCashFlow: 14500, savingsRate: 54.7 },
    { month: "2026-08", income: 26500, expenses: 12249, netCashFlow: 14251, savingsRate: 49.2 },
    { month: "2026-09", income: 26500, expenses: 12354, netCashFlow: 14146, savingsRate: 53.4 },
  ],
  topMerchants: [
    {
      merchant: "Cape Town apartment rent",
      searchQuery: "Cape Town apartment rent",
      primaryCategory: "Rent",
      spent: 8000,
      transactionCount: 1,
      sharePercent: 64.8,
      previousMonthSpent: 8000,
      monthOverMonthChange: 0,
    },
    {
      merchant: "Checkers",
      searchQuery: "Checkers",
      primaryCategory: "Groceries",
      spent: 1850,
      transactionCount: 3,
      sharePercent: 15.0,
      previousMonthSpent: 1550,
      monthOverMonthChange: 300,
    },
  ],
  warningSettings: { enabled: true, thresholdPercent: 80 },
  warnings: [],
};

test("interactive visualizations, savings rate, top merchants drill-down, and rollover toggle", async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem("token", "insights-browser-test");
    localStorage.setItem("month", "2026-09");
  });

  let savedRolloverPayload: { categoryLimits?: { category: string; rolloverEnabled?: boolean }[] } | null = null;

  await page.route("**/api/budget/analytics?*", (route) =>
    route.fulfill({ json: SAMPLE_ANALYTICS }),
  );
  await page.route("**/api/budget/set", async (route) => {
    savedRolloverPayload = route.request().postDataJSON();
    return route.fulfill({ json: SAMPLE_ANALYTICS });
  });
  await page.route("**/api/recurring-bills/occurrences?*", (route) =>
    route.fulfill({ json: [] }),
  );
  await page.route("**/api/transaction/net-cash-flow?*", (route) =>
    route.fulfill({ json: { netCashFlow: 14146 } }),
  );
  await page.route("**/api/transaction/categories", (route) =>
    route.fulfill({ json: ["Groceries", "Rent", "Utilities", "Transport"] }),
  );
  await page.route("**/api/category-rules", (route) =>
    route.fulfill({ json: [] }),
  );
  await page.route(/\/api\/transaction(\?|$)/, (route) => {
    const search = new URL(route.request().url()).searchParams.get("search");
    const rows =
      search === "Checkers"
        ? [{ id: 1, date: "2026-09-03", description: "Checkers groceries", amount: -1850, category: "Groceries" }]
        : [];
    return route.fulfill({ json: rows });
  });

  await page.goto("/");

  // Savings Rate KPI card
  const savingsCard = page.getByRole("region", { name: "Savings rate", exact: true });
  await expect(savingsCard).toContainText("53.4%");
  await expect(savingsCard).toContainText("+4.2 pp vs last month");

  // Interactive Category Breakdown donut & MoM/YoY toggle
  const breakdown = page.getByRole("region", { name: "Expense breakdown by category", exact: true });
  await expect(breakdown).toContainText("+12.0%");
  await breakdown.getByRole("button", { name: "vs Last year", exact: true }).click();
  await expect(breakdown).toContainText("-12.5%");

  const groceriesSlice = breakdown.getByRole("button", { name: /Groceries:/ });
  await groceriesSlice.click();
  const categoryTooltip = breakdown.getByRole("status", { name: "Category insight details" });
  await expect(categoryTooltip).toContainText("Groceries");
  await expect(categoryTooltip).toContainText(/rollover/i);
  await expect(categoryTooltip).toContainText("vs last month");
  await expect(categoryTooltip).toContainText("vs last year");

  // Interactive 6-Month Trends chart & Savings Rate view
  const trendsCard = page.getByRole("region", { name: "Spending trends", exact: true });
  await trendsCard.getByRole("button", { name: "Inspect 2026-06", exact: true }).click();
  const trendTooltip = trendsCard.getByRole("status", { name: "Trend month details" });
  await expect(trendTooltip).toContainText("2026-06");
  await expect(trendTooltip).toContainText("-18.0%");
  await trendsCard.getByRole("button", { name: "Savings Rate", exact: true }).click();
  await expect(trendsCard.getByRole("img", { name: "Six-month savings rate chart" })).toBeVisible();

  // Top Merchants card & drill-down to Transactions search
  const merchantsCard = page.getByRole("region", { name: "Top merchants", exact: true });
  await expect(merchantsCard).toContainText("Checkers");
  await expect(merchantsCard).toContainText("3 transactions");
  await merchantsCard.getByRole("link", { name: "Checkers", exact: true }).click();
  await expect(page.getByLabel("Description search", { exact: true })).toHaveValue("Checkers");
  await expect(page.getByRole("cell", { name: "Checkers groceries", exact: true })).toBeVisible();

  // Budgets page per-category rollover controls & breakdown
  await page.getByRole("link", { name: "Budgets", exact: true }).click();
  const groceriesRollover = page.getByLabel("Roll over unused funds for Groceries", { exact: true });
  const rentRollover = page.getByLabel("Roll over unused funds for Rent", { exact: true });
  await expect(groceriesRollover).toBeChecked();
  await expect(rentRollover).not.toBeChecked();
  await expect(page.getByText("Rollover enabled")).toBeVisible();
  await expect(page.getByText(/rollover =/)).toBeVisible();

  await rentRollover.check();
  await page.getByRole("button", { name: "Save budget", exact: true }).click();
  await expect(page.getByText("Budget saved.", { exact: true })).toBeVisible();
  expect(savedRolloverPayload?.categoryLimits?.find((c) => c.category === "Rent")?.rolloverEnabled).toBe(true);

  // Mobile layout safety (320px and 390px)
  await page.setViewportSize({ width: 320, height: 800 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.goto("/");
  await expect(page.getByRole("region", { name: "Top merchants", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
});
