import { chromium } from '@playwright/test';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs';

const root = process.cwd();
const chrome = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const html = path.join(root, 'docs/showcase-c/VibeDeck-Showcase-C.html');
const outPdf = path.join(root, 'docs/showcase-c/VibeDeck-Showcase-C-v4-glass.pdf');
const browser = await chromium.launch({ headless: true, executablePath: chrome });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(html).href, { waitUntil: 'networkidle' });
await page.evaluate(() => document.fonts.ready);

for (let i = 1; i <= 6; i++) {
  const section = page.locator(`#p${i}`);
  await section.screenshot({ path: path.join(root, `docs/showcase-c/page-${String(i).padStart(2,'0')}.png`) });
}

// Freeze the already-QA'd 1920x1080 renders into the final PDF instead of
// letting Chromium's print media re-layout the live HTML a second time.
// This guarantees the PDF matches the reviewed PNGs exactly.
const frozenPage = await browser.newPage({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
const imageUris = [];
for (let i = 1; i <= 6; i++) {
  const pngPath = path.join(root, `docs/showcase-c/page-${String(i).padStart(2,'0')}.png`);
  const b64 = fs.readFileSync(pngPath).toString('base64');
  imageUris.push(`data:image/png;base64,${b64}`);
}

await frozenPage.setContent(`<!doctype html>
<html><head><meta charset="utf-8"><style>
  @page { size: 13.333333in 7.5in; margin: 0; }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; background: #000; }
  .sheet { width: 1280px; height: 720px; margin: 0; padding: 0; page-break-after: always; overflow: hidden; }
  .sheet:last-child { page-break-after: auto; }
  img { display: block; width: 1280px; height: 720px; object-fit: fill; margin: 0; padding: 0; }
</style></head><body>
${imageUris.map(src => `<div class="sheet"><img src="${src}"></div>`).join('\n')}
</body></html>`, { waitUntil: 'load' });

await frozenPage.pdf({
  path: outPdf,
  printBackground: true,
  preferCSSPageSize: true,
  margin: { top: 0, right: 0, bottom: 0, left: 0 }
});

await browser.close();
