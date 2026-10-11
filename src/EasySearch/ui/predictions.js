window.setupPredictions = (input, mirror, host) => {
  const ghost = document.querySelector('#query-prediction') || document.createElement('span');
  ghost.id = 'query-prediction'; ghost.className = 'query-prediction';
  ghost.setAttribute('aria-hidden', 'true');
  if (!ghost.isConnected) input.parentElement.append(ghost);
  const filters = ['inside:', 'type:', 'ext:', 'kind:', 'size:', 'date:', 'in:', 'name:', 'path:', 'regex:', 'modified:', 'source:', 'location:', 'where:', 'under:', 'parent:', 'folders:', 'files:', 'folder:'];
  let key = '', names = [], selected = 0, context, composing = false, timer, results = [];
  function current() {
    if (composing || document.activeElement !== input || input.selectionStart !== input.selectionEnd || input.selectionEnd !== input.value.length) return null;
    const value = input.value;
    const match = /(?:^|[\s(|])(?:inside|in|foldername):("[^"]*"?|[^\s()|]*)$/i.exec(value);
    if (match && (value.slice(0, match.index).match(/"/g) || []).length % 2 === 0) {
      const raw = match[1], typed = raw.replace(/^"|"$/g, '');
      return typed && !/[\\/:]/.test(typed) ? { key: `folder:${typed}`, typed, raw, start: value.length - raw.length, folder: true } : null;
    }
    if ((value.match(/"/g) || []).length % 2) return null;
    const token = /[^\s()|]+$/.exec(value);
    if (!token || /["'!:]/.test(token[0])) return null;
    return { key: `text:${value}`, typed: token[0], raw: token[0], start: token.index };
  }
  function render() {
    ghost.textContent = ''; context = current();
    if (!context) return;
    if (context.key !== key) {
      key = context.key; selected = 0; names = []; clearTimeout(timer);
      if (context.folder) {
        const prefix = context.typed;
        timer = setTimeout(() => host.post({ type: 'predictFolder', prefix }), 80);
      }
    }
    if (!context.folder) names = [...new Set([...filters, ...results])]
      .filter(name => name.toLocaleLowerCase().startsWith(context.typed.toLocaleLowerCase()) && name.length > context.typed.length)
      .sort((a, b) => a.length - b.length || a.localeCompare(b));
    if (!names.length) return;
    selected %= names.length;
    const name = names[selected];
    context.replacement = context.folder && (/[\s()|]/.test(name) || context.raw.startsWith('"')) ? `"${name}"` : name;
    const suffix = context.replacement.toLocaleLowerCase().startsWith(context.raw.toLocaleLowerCase())
      ? context.replacement.slice(context.raw.length) : ` → ${name}`;
    if (!suffix && names.length === 1) return;
    const target = [...mirror.querySelectorAll('[data-query-start]')].find(el => input.value.length >= +el.dataset.queryStart && input.value.length <= +el.dataset.queryEnd);
    if (!target) return;
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode(), offset = input.value.length - +target.dataset.queryStart;
    while (node && offset > node.length) { offset -= node.length; node = walker.nextNode(); }
    if (!node) return;
    const range = document.createRange(); range.setStart(node, offset); range.collapse(true);
    ghost.style.left = `${range.getBoundingClientRect().left - mirror.getBoundingClientRect().left}px`;
    ghost.textContent = `${suffix || ` → ${name}`}${names.length > 1 ? `  (${selected + 1}/${names.length})` : ''}`;
    ghost.title = '←/↑ previous · →/↓ next · Tab accept';
  }
  const onMessage = message => {
    if (message.type === 'folderPrediction' && key === `folder:${message.prefix}`) {
      names = message.names || []; selected = 0; render();
    } else if (message.type === 'results') {
      results = (message.items || []).filter(item => !item.command).map(item => item.name); render();
    }
  };
  document.addEventListener('keydown', event => {
    if (event.target !== input || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || event.isComposing) return;
    render();
    if (!ghost.textContent || !context) return;
    const direction = { ArrowLeft: -1, ArrowUp: -1, ArrowRight: 1, ArrowDown: 1 }[event.key];
    if (direction && names.length > 1) {
      event.preventDefault(); event.stopImmediatePropagation();
      selected = (selected + direction + names.length) % names.length; render();
    } else if (event.key === 'Tab') {
      event.preventDefault(); event.stopImmediatePropagation();
      input.setRangeText(context.replacement, context.start, input.value.length, 'end');
      input.dispatchEvent(new Event('input', { bubbles: true })); render();
    }
  }, true);
  for (const event of ['input', 'keyup', 'click', 'focus', 'scroll', 'select']) input.addEventListener(event, render);
  input.addEventListener('blur', () => { ghost.textContent = ''; });
  input.addEventListener('compositionstart', () => { composing = true; render(); });
  input.addEventListener('compositionend', () => { composing = false; render(); });
  document.addEventListener('selectionchange', render);
  new ResizeObserver(render).observe(input);
  return onMessage;
};
