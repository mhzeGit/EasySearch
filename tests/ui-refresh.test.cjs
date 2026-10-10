// Run with: node --test tests/ui-refresh.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const source = readFileSync(require('node:path').join(__dirname, '../src/EasySearch/ui/app.js'), 'utf8');

function setup() {
  let nextTimer = 1;
  const timers = new Map(), posts = [], renders = [];
  const context = vm.createContext({
    state: {
      status: { version: 1, indexing: false, items: 200, scanned: 0 },
      searchedVersion: 1, searchedItems: 200, searchedScanned: 0,
      view: 'main', pending: false, keepSelection: true, searchId: 1,
      items: [item('a'), item('b')], total: 2, selected: 1, error: null,
      renderedQuery: '', query: '', settings: {}, sort: 'name', sortDirections: {}, options: {},
    },
    SORTS: [{ id: 'name' }],
    menu: { isOpen: false },
    els: { query: { value: '' } }, document: {}, searchTimer: 0,
    host: { post: message => posts.push(message) },
    pathOf: item => item.dir + '\\' + item.name,
    renderList: options => { renders.push(options); context.state.renderedQuery = context.state.query; },
    renderCount() {}, renderPrediction() {}, renderStatus() {},
    selectedItem: () => context.state.items[context.state.selected],
    setTimeout: fn => { const id = nextTimer++; timers.set(id, fn); return id; },
    clearTimeout: id => timers.delete(id),
  });
  const searching = source.slice(source.indexOf('  function runSearch('), source.indexOf('  // ---- home'));
  const refresh = source.slice(source.indexOf('  let refreshTimer = 0;'), source.indexOf('  host.listen('));
  vm.runInContext(searching + refresh, context);
  return { context, state: context.state, timers, posts, renders,
    tick() { const [id, fn] = timers.entries().next().value; timers.delete(id); fn(); } };
}
const item = name => ({ name, dir: 'C:\\test', modified: 1, size: 10 });

test('background partial rankings cannot remove rows or move the selection', () => {
  const t = setup();
  t.context.runSearch({ keepSelection: true });
  t.context.onResults({ id: 2, partial: true, items: [item('a')], total: 1 });
  assert.equal(t.state.items.length, 2);
  assert.equal(t.state.selected, 1);
  assert.equal(t.state.pending, true);
  assert.equal(t.renders.length, 0);
  t.context.onResults({ id: 2, items: [item('a'), item('b')], total: 2 });
  assert.equal(t.state.pending, false);
  assert.equal(t.renders.length, 0);
});

test('new results are applied once, preserving the selected path and scroll', () => {
  const t = setup();
  t.context.runSearch({ keepSelection: true });
  t.context.onResults({ id: 2, items: [item('new'), item('a'), item('b')], total: 3 });
  assert.equal(t.state.selected, 2);
  assert.equal(t.renders.length, 1);
  assert.equal(t.renders[0].keepScroll, true);
  t.context.onResults({ id: 2, partial: true, items: [item('a')], total: 1 });
  assert.equal(t.state.items.length, 3);
  assert.equal(t.renders.length, 1);
});

test('initial searches still stream partial results', () => {
  const t = setup();
  t.context.runSearch();
  t.context.onResults({ id: 2, partial: true, items: [item('a')], total: 1 });
  assert.equal(t.state.items.length, 1);
  assert.equal(t.renders.length, 1);
});

test('unchanged indexing status never schedules a search; progress is throttled without starvation', () => {
  const t = setup();
  const status = { ...t.state.status, indexing: true };
  for (let i = 0; i < 10; i++) t.context.onStatus(status);
  assert.equal(t.timers.size, 0);
  t.context.onStatus({ ...status, scanned: 10 });
  const timer = [...t.timers.keys()][0];
  t.context.onStatus({ ...status, scanned: 20 });
  assert.deepEqual([...t.timers.keys()], [timer]);
  t.tick();
  assert.equal(t.posts.length, 1);
  assert.equal(t.state.searchedScanned, 20);
});

test('index changes during a pending search schedule a follow-up when it completes', () => {
  const t = setup();
  t.context.runSearch({ keepSelection: true });
  t.context.onStatus({ ...t.state.status, version: 2 });
  assert.equal(t.timers.size, 0);
  t.context.onResults({ id: 2, items: [item('a'), item('b')], total: 2 });
  assert.equal(t.timers.size, 1);
  t.tick();
  assert.equal(t.posts.length, 2);
});

test('a user search cancels a scheduled refresh and stale responses are ignored', () => {
  const t = setup();
  t.context.onStatus({ ...t.state.status, version: 2 });
  assert.equal(t.timers.size, 1);
  t.context.newSearch();
  assert.equal(t.timers.size, 0);
  t.context.onResults({ id: 1, items: [], total: 0 });
  assert.equal(t.state.items.length, 2);
});
