import { chromium } from '@playwright/test';
import path from 'node:path';

const chrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const browser = await chromium.launch({ headless: true, executablePath: chrome });
const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
await page.goto('http://127.0.0.1:5011/?lang=en', { waitUntil: 'networkidle' });
await page.click('#setupMode');
await page.locator('details.appearance-theme-panel').evaluate((el) => { el.open = true; });
await page.waitForTimeout(300);

const panel = page.locator('details.appearance-theme-panel');
await panel.screenshot({ path: path.resolve('docs/showcase-c/live-theme-settings.png') });

await browser.close();
