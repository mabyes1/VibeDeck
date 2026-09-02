import { expect, test } from "@playwright/test";

const host = process.env.VIBEDECK_TEST_URL || "http://127.0.0.1:5000";

test("Device Lab previews Stock Watch as a real fullscreen Custom Deck", async ({ page }) => {
  await page.goto(`${host}/device-lab.html?device=galaxy-s23&mode=deck&deck=stock-watch`, {
    waitUntil: "domcontentloaded",
  });

  await expect(page.locator("#modeSelect")).toHaveValue("deck");
  await expect(page.locator("#deckControl")).toBeVisible();
  await expect(page.locator("#deckSelect")).toHaveValue("stock-watch");

  const preview = page.frameLocator("#previewFrame");
  await expect(preview.locator("body.deck-viewer.viewer-immersive.mode-deck")).toHaveCount(1, { timeout: 5000 });
  await expect(preview.locator("#customDeckFrame")).toBeVisible({ timeout: 5000 });

  const deck = preview.frameLocator("#customDeckFrame");
  await expect(deck.locator("h1")).toHaveText("STOCK WATCH", { timeout: 5000 });
  await expect(deck.getByRole("button", { name: "設定" })).toBeVisible();
  await expect(deck.locator("#cardCountSelect")).toHaveCount(1);

  const src = await page.locator("#previewFrame").getAttribute("src");
  expect(src).toContain("mode=deck");
  expect(src).toContain("deck=stock-watch");
  expect(src).toContain("viewer=1");
});

test("Device Lab passes iPhone safe-area environment into sandboxed Custom Deck", async ({ page }) => {
  await page.goto(`${host}/device-lab.html?device=iphone-xs&mode=deck&deck=stock-watch`, {
    waitUntil: "domcontentloaded",
  });

  const preview = page.frameLocator("#previewFrame");
  const deck = preview.frameLocator("#customDeckFrame");
  await expect(deck.locator("h1")).toHaveText("STOCK WATCH", { timeout: 5000 });

  const environment = await deck.locator("html").evaluate(root => ({
    safeBottom: getComputedStyle(root).getPropertyValue("--vibedeck-safe-area-bottom").trim(),
    deckPaddingBottom: getComputedStyle(document.querySelector(".stock-deck")).paddingBottom,
  }));
  expect(environment.safeBottom).toBe("34px");
  expect(environment.deckPaddingBottom).toBe("5px");
});

test("iPhone XS landscape Stock Watch cards fill the quote viewport", async ({ page }) => {
  await page.goto(`${host}/device-lab.html?device=iphone-xs&mode=deck&deck=stock-watch`, {
    waitUntil: "domcontentloaded",
  });
  await page.locator("#orientationSelect").selectOption("landscape");

  const preview = page.frameLocator("#previewFrame");
  const deck = preview.frameLocator("#customDeckFrame");
  await expect(preview.locator("body.deck-viewer.viewer-immersive.mode-deck")).toHaveCount(1, { timeout: 5000 });
  await deck.getByRole("button", { name: "設定" }).click();
  await expect(deck.locator("#cardCountSelect")).toBeVisible({ timeout: 5000 });
  await deck.locator("#cardCountSelect").selectOption("8");
  await expect(deck.locator(".quote-card")).toHaveCount(8, { timeout: 5000 });

  const geometry = await deck.locator("body").evaluate(() => {
    const viewport = document.querySelector("#quoteViewport")?.getBoundingClientRect();
    const list = document.querySelector("#quoteList");
    const cards = [...document.querySelectorAll(".quote-card")].map(card => card.getBoundingClientRect());
    const lastBottom = cards.length ? Math.max(...cards.map(rect => rect.bottom)) : 0;
    return {
      columns: Number.parseInt(getComputedStyle(list).getPropertyValue("--stock-columns"), 10),
      viewportHeight: viewport?.height || 0,
      unusedBottom: viewport ? viewport.bottom - lastBottom : 999,
    };
  });

  expect(geometry.columns).toBe(4);
  expect(geometry.viewportHeight).toBeGreaterThan(200);
  expect(geometry.unusedBottom).toBeGreaterThanOrEqual(-1);
  expect(geometry.unusedBottom).toBeLessThanOrEqual(6);
});
