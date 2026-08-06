import { expect, test } from "@playwright/test";

test("loads the editor shell from the GitHub Pages base path", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveTitle("Shadowbus 配置工作台");
  await expect(page.getByRole("button", { name: "打开 Mods 目录" })).toBeVisible();
  await expect(page.getByText("AIData", { exact: true })).toBeVisible();
  await expect(page.getByText("CardMaster", { exact: true })).toBeVisible();
  await expect(page.getByText("TwoPick", { exact: true })).toBeVisible();
});

