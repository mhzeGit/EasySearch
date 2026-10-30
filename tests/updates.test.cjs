const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/app.js', route => route.fulfill({ contentType: 'application/javascript', body: `
      const create = window.createMockHost;
      window.createMockHost = () => {
        const mock = create();
        const updates = window.updateTest = { posts: [], receive: null };
        return {
          listen(fn) { updates.receive = fn; mock.listen(fn); },
          post(message) {
            if (['checkUpdate', 'installUpdate'].includes(message.type)) updates.posts.push(message.type);
            else mock.post(message);
          }
        };
      };
    ` + fs.readFileSync('src/EasySearch/ui/app.js', 'utf8') }));
    await page.goto(pathToFileURL(path.resolve('src/EasySearch/ui/index.html')).href);
    await page.locator('#btn-settings').click();
    await page.locator('[data-tab="about"]').click();
    await page.locator('#update').click();
    assert.deepEqual(await page.evaluate(() => window.updateTest.posts), ['checkUpdate']);
    await page.evaluate(() => window.updateTest.receive({ type: 'updateState', busy: true, available: false, text: 'Checking GitHub…' }));
    assert.equal(await page.locator('#update').isDisabled(), true);
    await page.evaluate(() => window.updateTest.receive({ type: 'updateState', busy: false, available: true, text: 'A new build (0.1.0) is available.' }));
    assert.equal(await page.locator('#update').innerText(), 'Update & restart');
    await page.locator('#update').click();
    assert.deepEqual(await page.evaluate(() => window.updateTest.posts), ['checkUpdate', 'installUpdate']);
    await page.evaluate(() => window.updateTest.receive({ type: 'updateState', busy: false, available: false, text: "You're up to date." }));
    assert.equal(await page.locator('[role="status"]').filter({ hasText: "You're up to date." }).count(), 1);
    assert.equal(await page.locator('#update').innerText(), 'Check for updates');
    assert.deepEqual(errors, []);
    if (process.env.UPDATE_SCREENSHOT) await page.screenshot({ path: process.env.UPDATE_SCREENSHOT });
    console.log('Update checks, busy state, install action, status messages and retry button passed.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
