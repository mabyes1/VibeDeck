import { expect, test } from "@playwright/test";

const host = process.env.VIBEDECK_TEST_URL || "http://127.0.0.1:5000";

test("Device Lab previews the bundled Custom Deck without a product-specific dependency", async ({ page }) => {
  await page.goto(`${host}/device-lab.html?device=galaxy-s23&mode=deck&deck=coding-pet`, {
    waitUntil: "domcontentloaded",
  });

  await expect(page.locator("#modeSelect")).toHaveValue("deck");
  await expect(page.locator("#deckControl")).toBeVisible();
  await expect(page.locator("#deckSelect")).toHaveValue("coding-pet");

  const preview = page.frameLocator("#previewFrame");
  await expect(preview.locator("body.deck-viewer.viewer-immersive.mode-deck")).toHaveCount(1, { timeout: 5000 });
  await expect(preview.locator("#customDeckFrame")).toBeVisible({ timeout: 5000 });

  const deck = preview.frameLocator("#customDeckFrame");
  await expect(deck.locator("h1")).toHaveText("Coding Pet", { timeout: 5000 });
  await expect(deck.getByRole("button", { name: "Boop the cat" })).toBeVisible();

  const src = await page.locator("#previewFrame").getAttribute("src");
  expect(src).toContain("mode=deck");
  expect(src).toContain("deck=coding-pet");
  expect(src).toContain("viewer=1");
});
