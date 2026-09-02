import { expect, test } from "@playwright/test";

const host = process.env.VIBEDECK_TEST_URL || "http://127.0.0.1:5000";

test("Stock Watch bridge becomes live quickly and DBG exposes timing", async ({ page }) => {
  await page.setViewportSize({ width: 780, height: 360 });
  await page.goto(`${host}/index.html?mode=deck&deck=stock-watch&preview=${Date.now()}`, {
    waitUntil: "domcontentloaded",
  });

  const stockTab = page.getByRole("button", { name: /Stock Watch/ });
  await expect(stockTab).toBeVisible({ timeout: 5000 });
  await stockTab.click();

  const frame = page.frameLocator("#customDeckFrame");
  await expect(frame.locator("#connection")).toHaveText("行情連線", { timeout: 3000 });
  await expect(frame.locator("#summary")).toHaveText("共 35 檔", { timeout: 3000 });
  await expect(frame.locator(".quote-card").first()).toBeVisible({ timeout: 3000 });
  await expect(frame.locator(".quote-card .deal").first()).not.toHaveText("--", { timeout: 3000 });

  await frame.getByRole("button", { name: "設定" }).click();
  await frame.locator("#debugToggle").click();
  await expect(frame.locator("#debugPanel")).toBeVisible();
  await expect(frame.locator("#debugSummary")).toContainText("35/35", { timeout: 4000 });
  await expect(frame.locator("#debugLog")).toContainText("[bridge]", { timeout: 4000 });
});

test("Stock Watch phone portrait packs eight cards per page", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 });
  await page.goto(`${host}/index.html?mode=deck&deck=stock-watch&preview=${Date.now()}`, {
    waitUntil: "domcontentloaded",
  });

  const stockTab = page.getByRole("button", { name: /Stock Watch/ });
  await expect(stockTab).toBeVisible({ timeout: 5000 });
  await stockTab.click();

  const frame = page.frameLocator("#customDeckFrame");
  await expect(frame.locator("#summary")).toHaveText("共 35 檔", { timeout: 3000 });
  await expect(frame.locator(".quote-card")).toHaveCount(8, { timeout: 3000 });

  const clipped = await frame.locator(".quote-card").evaluateAll(cards => cards.some(card =>
    card.scrollHeight > card.clientHeight + 1 || card.scrollWidth > card.clientWidth + 1
  ));
  expect(clipped).toBe(false);
});

test("Stock Watch wider portrait iframe still packs eight cards", async ({ page }) => {
  await page.setViewportSize({ width: 540, height: 720 });
  await page.goto(`${host}/index.html?mode=deck&deck=stock-watch&preview=${Date.now()}`, {
    waitUntil: "domcontentloaded",
  });

  const stockTab = page.getByRole("button", { name: /Stock Watch/ });
  await expect(stockTab).toBeVisible({ timeout: 5000 });
  await stockTab.click();

  const frame = page.frameLocator("#customDeckFrame");
  await expect(frame.locator("#summary")).toHaveText("共 35 檔", { timeout: 3000 });
  await expect(frame.locator(".quote-card")).toHaveCount(8, { timeout: 3000 });
});

test("Stock Watch card count selector controls exact page size", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 });
  await page.goto(`${host}/index.html?mode=deck&deck=stock-watch&preview=${Date.now()}`, {
    waitUntil: "domcontentloaded",
  });

  const stockTab = page.getByRole("button", { name: /Stock Watch/ });
  await expect(stockTab).toBeVisible({ timeout: 5000 });
  await stockTab.click();

  const frame = page.frameLocator("#customDeckFrame");
  await expect(frame.locator("#summary")).toHaveText("共 35 檔", { timeout: 3000 });
  await frame.getByRole("button", { name: "設定" }).click();
  await frame.locator("#cardCountSelect").selectOption("9");
  await expect(frame.locator(".quote-card")).toHaveCount(9);
  await frame.locator("#cardCountSelect").selectOption("6");
  await expect(frame.locator(".quote-card")).toHaveCount(6);
  await frame.locator("#cardCountSelect").selectOption("8");
  await expect(frame.locator(".quote-card")).toHaveCount(8);
});

test("Stock Watch sorts the full watchlist by gainers and losers", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 });
  await page.goto(`${host}/index.html?mode=deck&deck=stock-watch&preview=${Date.now()}`, {
    waitUntil: "domcontentloaded",
  });

  await page.getByRole("button", { name: /Stock Watch/ }).click();
  const frame = page.frameLocator("#customDeckFrame");
  await expect(frame.locator("#summary")).toHaveText("共 35 檔", { timeout: 3000 });

  await frame.locator("#sortSelect").selectOption("gainers");
  const gainers = await frame.locator(".quote-card .diff").allTextContents();
  expect(Number.parseFloat(gainers[0])).toBeGreaterThanOrEqual(Number.parseFloat(gainers.at(-1)));

  await frame.locator("#sortSelect").selectOption("losers");
  const losers = await frame.locator(".quote-card .diff").allTextContents();
  expect(Number.parseFloat(losers[0])).toBeLessThanOrEqual(Number.parseFloat(losers.at(-1)));
});
