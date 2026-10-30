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
    const query = page.locator('#query');
    const chips = page.locator('.query-filter-chip');
    await query.press('Space');
    assert.equal(await query.inputValue(), 'report ');
    assert.equal(await chips.count(), 0);
    await query.pressSequentially('annual');
    assert.equal(await query.inputValue(), 'report annual');
    assert.equal(await chips.count(), 0);
    await query.press('Enter');
    assert.equal(await query.inputValue(), '');
    assert.equal(await chips.count(), 2);
    assert.deepEqual(await chips.locator('.filter-edit').allTextContents(), ['report', 'annual']);
    await page.locator('#query').fill('inside:Proj');
    await page.waitForTimeout(300);
    assert.deepEqual(errors, []);
    console.log('Browser startup, search, Space input and Enter commits passed without JavaScript errors.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
