import { test, expect } from "@playwright/test";
import { mkdir } from "node:fs/promises";
import path from "node:path";
test("capture deterministic portfolio screens", async ({ page }) => {
  await mkdir("../docs/screenshots", { recursive: true });
  await page.goto("/login");
  await page.getByLabel("Username", { exact: true }).fill("portfolio-demo");
  await page.getByLabel("Password", { exact: true }).fill("Demo-Rands-2026!");
  await page.getByRole("button", { name: "Login", exact: true }).click();
  await page.getByLabel("Month", { exact: true }).fill("2026-09");
  await expect(
    page.getByRole("link", { name: "Groceries", exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: "../docs/screenshots/dashboard.png",
    fullPage: true,
  });
  await page.getByRole("link", { name: "Budgets", exact: true }).click();
  await expect(page.getByLabel("Groceries limit (ZAR)")).toBeVisible();
  await page.screenshot({
    path: "../docs/screenshots/budgets.png",
    fullPage: true,
  });
  await page.getByRole("link", { name: "Transactions", exact: true }).click();
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page
    .getByRole("button", { name: "Import statement", exact: true })
    .click();
  await page
    .getByLabel("Statement file")
    .setInputFiles(path.resolve("../demo/import-example.csv"));
  await page
    .getByRole("button", { name: "Preview statement", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.screenshot({
    path: "../docs/screenshots/import-review.png",
    fullPage: true,
  });
  await page.keyboard.press("Escape");
  await page.getByRole("link", { name: "Dashboard", exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("link", { name: "Groceries", exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: "../docs/screenshots/mobile.png",
    fullPage: true,
  });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBeTruthy();
});
