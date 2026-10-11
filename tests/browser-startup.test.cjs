const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(pathToFileURL(path.resolve('src/EasySearch/ui/index.html')).href);
    await page.locator('#query').fill('report');
    await page.waitForFunction(() => document.querySelectorAll('#list .row').length > 0);
    assert.equal(await page.locator('.searchbar-icon svg').count(), 1);
    await page.locator('#query').fill('inside:Proj');
    await page.waitForTimeout(300);
    assert.deepEqual(errors, []);
    console.log('Browser startup, icons, search results and filter input passed without JavaScript errors.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
