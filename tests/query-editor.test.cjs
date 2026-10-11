// Run with: node --test tests/query-editor.test.cjs
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const source = readFileSync(require('node:path').join(__dirname, '../src/EasySearch/ui/app.js'), 'utf8');

function editor(value) {
  const query = { value, focus() {}, setSelectionRange(start, end) { this.selection = [start, end]; } };
  const context = vm.createContext({ els: { query }, state: {}, syncQuery() {},
    renderQueryHighlight() {}, syncQueryLayout() {}, newSearch() {} });
  vm.runInContext(source.slice(source.indexOf('  function queryTokens('), source.indexOf('  function syncQueryLayout(')), context);
  vm.runInContext(source.slice(source.indexOf('  function removeFilterToken('), source.indexOf('  function insertFilterToken(')), context);
  return { query, tokens: () => Array.from(context.queryTokens(query.value), token => ({ ...token })),
    remove(text) { const start = query.value.indexOf(text); context.removeFilterToken(start, start + text.length); } };
}

test('only whole filters outside quoted names become controls', () => {
  const e = editor('prototype:pdf "type:png OR folder" (type:pdf OR ext:docx) NOT draft do not disturb');
  assert.deepEqual(e.tokens().map(t => t.text), ['type:pdf', 'OR', 'ext:docx', 'NOT']);
});

test('quoted paths keep parentheses, spaces, and operator words intact', () => {
  const value = 'in:"C:\\Annual  reports (OR drafts)" AND size:>10mb';
  const e = editor(value);
  assert.deepEqual(e.tokens().map(t => t.text), ['in:"C:\\Annual  reports (OR drafts)"', 'AND', 'size:>10mb']);
  for (const token of e.tokens()) assert.equal(value.slice(token.start, token.end), token.text);
});

test('unfinished quoted values remain a single editable filter', () => {
  assert.deepEqual(editor('report in:"C:\\My folder').tokens().map(t => t.text), ['in:"C:\\My folder']);
});

test('filter syntax recognizes the families used by colored query labels', () => {
  const tokens = editor('type:pdf size:>10mb date:today in:"C:\\Reports" foldername:annual').tokens();
  assert.deepEqual(tokens.filter(t => t.key).map(t => t.key), ['type', 'size', 'date', 'in', 'foldername']);
  for (const token of tokens.filter(t => t.key)) assert.equal(token.text.slice(0, token.text.indexOf(':') + 1), `${token.key}:`);
});

test('removing a filter preserves significant whitespace inside quoted names and paths', () => {
  const e = editor('"annual  report" AND type:pdf AND in:"C:\\My  Files"');
  e.remove('type:pdf');
  assert.equal(e.query.value, '"annual  report" AND in:"C:\\My  Files"');
});

test('removing either alternative keeps its group valid', () => {
  const a = editor('(type:pdf OR ext:docx) AND draft');
  a.remove('type:pdf');
  assert.equal(a.query.value, '(ext:docx) AND draft');
  const b = editor('(type:pdf OR ext:docx) AND draft');
  b.remove('ext:docx');
  assert.equal(b.query.value, '(type:pdf) AND draft');
});

test('removing a lone grouped filter also removes its negation and adjacent operator', () => {
  const e = editor('draft AND NOT ((type:pdf))');
  e.remove('type:pdf');
  assert.equal(e.query.value, 'draft');
  assert.deepEqual(e.query.selection, [5, 5]);
});

test('repeated filters remove only the selected occurrence', () => {
  const e = editor('type:pdf OR type:pdf AND final');
  e.remove('type:pdf');
  assert.equal(e.query.value, 'type:pdf AND final');
});
