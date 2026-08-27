import { chromium } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const root = path.resolve(import.meta.dirname, "..");
const source = path.join(root, "docs", "assets", "producthunt", "source", "gallery.html");
const output = path.join(root, "docs", "assets", "producthunt");
const pages = ["hero", "custom-deck", "sideboard", "eink", "display"];
const staleOutputs = [
  "02-sideboard.png",
  "03-display.png",
  "04-deck.png",
  "04-display.png",
  "05-summary.png",
];

for (const name of staleOutputs) {
  fs.rmSync(path.join(output, name), { force: true });
}

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
});
const page = await browser.newPage({ viewport: { width: 1270, height: 760 }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(source).href);
await page.evaluate(() => document.fonts.ready);

for (let index = 0; index < pages.length; index += 1) {
  const id = pages[index];
  const element = page.locator(`#${id}`);
  await element.screenshot({
    path: path.join(output, `${String(index + 1).padStart(2, "0")}-${id}.png`),
    animations: "disabled",
  });
}

await browser.close();
console.log(`Rendered ${pages.length} Product Hunt gallery images to ${output}`);
