// Run with: node --test tests/video-preview.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const source = readFileSync(require('node:path').join(__dirname, '../src/EasySearch/ui/video-preview.js'), 'utf8');

function setup({ stall = false, duration = 100 } = {}) {
  const messages = [], videos = [], canvases = [], timers = new Map();
  let nextTimer = 0;
  class Video extends EventTarget {
    constructor() { super(); this.duration = duration; this.videoWidth = 3840; this.videoHeight = 2160; this.time = 0; this.seeks = []; }
    set src(value) { this.url = value; if (!stall) queueMicrotask(() => this.dispatchEvent(new Event('loadeddata'))); }
    get currentTime() { return this.time; }
    set currentTime(value) { this.time = value; this.seeks.push(value); queueMicrotask(() => this.dispatchEvent(new Event('seeked'))); }
    pause() { this.paused = true; }
    removeAttribute() { this.url = null; }
    load() { this.released = true; }
    remove() {}
  }
  const context = vm.createContext({ AbortController, setTimeout(fn, ms) { timers.set(++nextTimer, { fn, ms }); return nextTimer; },
    clearTimeout(id) { timers.delete(id); }, setInterval(fn, ms) { timers.set(++nextTimer, { fn, ms, interval: true }); return nextTimer; },
    clearInterval(id) { timers.delete(id); }, document: { createElement(tag) {
      if (tag === 'video') { const video = new Video(); videos.push(video); return video; }
      const canvas = { width: 0, height: 0, style: {}, setAttribute() {}, draws: [], getContext() {
        return { drawImage(image) { canvas.draws.push(image.time ?? image); } };
      } };
      canvases.push(canvas); return canvas;
    } } });
  vm.runInContext(source + '\nglobalThis.preview = new VideoPreview(message => messages.push(message));',
    vm.createContext(Object.assign(context, { messages })));
  const preview = context.preview;
  const hero = { replaceChildren(child) { this.child = child; } };
  return { preview, messages, videos, canvases, timers, hero,
    debounce() { for (const [id, timer] of timers) if (timer.ms === 120) { timers.delete(id); timer.fn(); } },
    async settle() { for (let i = 0; i < 30; i++) await Promise.resolve(); } };
}

test('passing selections never request media; a settled selection requests only its own file', () => {
  const e = setup();
  e.preview.select('first.mp4', e.hero);
  e.preview.select('second.mp4', e.hero);
  e.preview.select('third.mp4', e.hero);
  e.debounce();
  assert.deepEqual(e.messages.filter(m => m.path).map(m => m.path), ['third.mp4']);
  e.preview.clear();
  assert.equal(e.timers.size, 0);
});

test('preloads five bounded frames, closes the decoder, and loops at 500ms', async () => {
  const e = setup();
  e.preview.select('video.mp4', e.hero);
  e.debounce();
  e.preview.source({ id: e.preview.active.id, url: 'media' });
  await e.settle();
  const active = e.preview.active;
  assert.equal(active.frames.length, 5);
  assert.deepEqual(e.videos[0].seeks, [20, 40, 60, 80]);
  assert.deepEqual(Array.from(active.frames, c => c.draws[0]), [0, 20, 40, 60, 80]);
  assert.ok(e.canvases.every(c => c.width === 512 && c.height === 288));
  assert.equal(e.videos[0].released, true);
  assert.equal(active.video, null);
  const loop = [...e.timers.values()].find(t => t.interval);
  assert.equal(loop.ms, 500);
  for (let i = 0; i < 5; i++) loop.fn();
  assert.equal(active.display.draws[0], active.display.draws[5]);
  e.preview.clear();
  assert.ok(e.canvases.every(c => c.width === 0 && c.height === 0));
  assert.equal(active.frames.length, 0);
  assert.equal(e.timers.size, 0);
});

test('selection during a stalled read releases old resources and ignores its late reply', async () => {
  const e = setup({ stall: true });
  e.preview.select('old.mp4', e.hero);
  e.debounce();
  const old = e.preview.active;
  e.preview.source({ id: old.id, url: 'old' });
  e.preview.select('new.mp4', e.hero);
  e.preview.source({ id: old.id, url: 'late' });
  await e.settle();
  assert.equal(old.abort.signal.aborted, true);
  assert.equal(e.videos[0].released, true);
  assert.equal(e.videos.length, 1);
  assert.notEqual(e.preview.active, old);
  e.preview.clear();
  assert.equal(e.timers.size, 0);
});

test('unseekable media and errors preserve the static thumbnail and clear timers', async () => {
  for (const duration of [0, Infinity, NaN]) {
    const e = setup({ duration });
    e.hero.child = 'thumbnail';
    e.preview.select('bad.mp4', e.hero);
    e.debounce();
    e.preview.source({ id: e.preview.active.id, url: 'bad' });
    await e.settle();
    assert.equal(e.preview.active, null);
    assert.equal(e.hero.child, 'thumbnail');
    assert.equal(e.timers.size, 0);
    assert.equal(e.videos[0].released, true);
  }
});
