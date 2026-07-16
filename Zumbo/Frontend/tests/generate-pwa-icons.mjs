import { resolve } from 'node:path';
import { chromium } from 'playwright-core';

const executablePath = process.env.CHROME_PATH || 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const sourceUrl = process.env.ZUMBO_MARK_URL || 'http://127.0.0.1:5177/shared/zumbo-mark.svg';
const outputDirectory = resolve(import.meta.dirname, '../shared');
const browser = await chromium.launch({ executablePath, headless: true });

try {
  for (const size of [192, 512]) {
    const page = await browser.newPage({ viewport: { width: size, height: size } });
    await page.setContent(`
      <style>
        html, body, img { width: 100%; height: 100%; margin: 0; }
        img { display: block; }
      </style>
      <img src="${sourceUrl}" alt="Zumbo">
    `);
    await page.locator('img').waitFor();
    await page.screenshot({
      path: resolve(outputDirectory, `zumbo-mark-${size}.png`),
      omitBackground: true
    });
    await page.close();
  }
} finally {
  await browser.close();
}
