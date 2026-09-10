import { chromium } from '@playwright/test';
import { pathToFileURL, fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const pages = [
  'a1-signal-cover.html', 'a2-signal-features.html',
  'b1-gallery-cover.html', 'b2-gallery-features.html',
  'c1-stage-cover.html', 'c2-stage-features.html'
];

const browser = await chromium.launch({
  headless: true,
  executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args: ['--disable-gpu', '--no-sandbox', '--allow-file-access-from-files']
});
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
for (const file of pages) {
  await page.goto(pathToFileURL(path.join(here, file)).href, { waitUntil: 'load' });
  await page.screenshot({ path: path.join(here, file.replace('.html', '.png')), fullPage: false });
}
await browser.close();

