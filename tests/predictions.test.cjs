const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const handlers = {}, inputHandlers = {};
const ghost = { isConnected: true, style: {}, setAttribute() {} };
const input = {
  value: 'report inside:Proj', selectionStart: 18, selectionEnd: 18,
  addEventListener(type, handler) { inputHandlers[type] = handler; },
  setRangeText(text, start, end) {
    this.value = this.value.slice(0, start) + text + this.value.slice(end);
    this.selectionStart = this.selectionEnd = this.value.length;
  },
  dispatchEvent() { inputHandlers.input(); }
};
const mirror = {
  querySelectorAll: () => [{ dataset: { queryStart: 0, queryEnd: 100 } }],
  getBoundingClientRect: () => ({ left: 0 })
};
let receive;
const context = {
  window: {}, setTimeout, clearTimeout, Event: class {}, NodeFilter: { SHOW_TEXT: 4 },
  ResizeObserver: class { observe() {} },
  document: {
    activeElement: input, querySelector: () => ghost,
    addEventListener(type, handler) { handlers[type] = handler; },
    createTreeWalker: () => ({ nextNode: () => ({ length: 100 }) }),
    createRange: () => ({ setStart() {}, collapse() {}, getBoundingClientRect: () => ({ left: 20 }) })
  }
};
vm.runInNewContext(fs.readFileSync('src/EasySearch/ui/predictions.js', 'utf8'), context);
receive = context.window.setupPredictions(input, mirror, { post() {} });
inputHandlers.input();
receive({ type: 'folderPrediction', prefix: 'Proj', names: ['Projects', 'Project Archive', 'Projector'] });
assert.match(ghost.textContent, /ects.*1\/3/);
function press(key) {
  let prevented = false;
  handlers.keydown({ key, target: input, preventDefault() { prevented = true; }, stopImmediatePropagation() {} });
  return prevented;
}
assert.equal(press('ArrowDown'), true);
assert.match(ghost.textContent, /Project Archive.*2\/3/);
assert.equal(press('ArrowRight'), true);
assert.match(ghost.textContent, /3\/3/);
assert.equal(press('ArrowRight'), true);
assert.match(ghost.textContent, /1\/3/);
press('ArrowUp'); press('ArrowLeft');
assert.match(ghost.textContent, /2\/3/);
press('Tab');
assert.equal(input.value, 'report inside:"Project Archive"');
input.value = 'inside:Projecs'; input.selectionStart = input.selectionEnd = input.value.length;
inputHandlers.input();
receive({ type: 'folderPrediction', prefix: 'Proj', names: ['Wrong'] });
assert.equal(ghost.textContent, '');
receive({ type: 'folderPrediction', prefix: 'Projecs', names: ['Projects'] });
press('Tab');
assert.equal(input.value, 'inside:Projects');
input.selectionStart = input.selectionEnd = 3;
assert.equal(press('ArrowLeft'), false);
inputHandlers.compositionstart();
assert.equal(press('Tab'), false);
console.log('Prediction navigation, wrapping, quoting, typo acceptance, stale responses, caret and IME checks passed.');
