window.setupPredictions = (input, mirror, host) => {
  const ghost = document.querySelector('#query-prediction') || document.createElement('span');
  ghost.id = 'query-prediction'; ghost.className = 'query-prediction';
  ghost.setAttribute('aria-hidden', 'true');
  if (!ghost.isConnected) input.parentElement.append(ghost);
  const examples = {};
  const define = (keys, choices) => keys.split(' ').forEach(key => examples[key] = choices);
  define('date dm modified datemodified changed updated', [
    ['today', 'Today'], ['yesterday', 'Yesterday'], ['week', 'This week'], ['month', 'This month'], ['year', 'This year'],
    ['7d', 'Last N days: Nd'], ['2w', 'Last N weeks: Nw'], ['3m', 'Last N months: Nm'], ['1y', 'Last N years: Ny'], ['24h', 'Last N hours: Nh'],
    ['2026-01-15', 'Exact day: YYYY-MM-DD'], ['2026-01', 'Whole month: YYYY-MM'], ['2026', 'Whole year: YYYY'],
    ['2026-01-01..2026-12-31', 'Between dates, inclusive: YYYY-MM-DD..YYYY-MM-DD'],
    ['>2026-01-01', 'From this date onward: >YYYY-MM-DD'], ['<2026-01-01', 'Before this date: <YYYY-MM-DD'],
    ['>=2026-01-01', 'On or after: >=YYYY-MM-DD'], ['<=2026-01-01', 'On or before: <=YYYY-MM-DD'],
    ['2026-01-01..', 'From date onward'], ['..2026-12-31', 'Through date, inclusive']
  ]);
  define('size', [['>10mb', 'Larger than: >number + unit'], ['<1gb', 'Smaller than: <number + unit'],
    ['1mb..1gb', 'Between sizes: minimum..maximum'], ['>=10mb', 'At least'], ['<=1gb', 'At most'],
    ['10mb', 'At least this size; units: b, kb, mb, gb, tb'], ['1.5gb', 'Decimal sizes'], ['1-10mb', 'Range with shared unit'],
    ...['empty', 'tiny', 'small', 'medium', 'large', 'huge', 'gigantic'].map(value => [value, 'Named size group'])]);
  define('type t kind', [['file', 'Files only'], ['folder', 'Folders only'], ['image', 'Images'], ['video', 'Videos'],
    ['audio', 'Audio'], ['document', 'Documents'], ['archive', 'Archives'], ['code', 'Source code'], ['pdf', 'PDF files'], ['image|video', 'Either type: type1|type2']]);
  define('ext', [['pdf', 'Extension without dot'], ['jpg|png', 'Either extension: ext1|ext2'], ['.txt', 'A leading dot is also accepted']]);
  define('inside foldername folder-name', [['Projects', 'Folder name; closest indexed matches as you type'], ['"Unity Projects"', 'Quote names containing spaces']]);
  define('in', [['Projects', 'Folder name'], ['"C:\\Users"', 'Full path; quote spaces']]);
  define('source location where under parent', [['"C:\\Users"', 'Full folder path; quote spaces'], ['"C:\\Program Files"', 'Search beneath this path']]);
  define('name', [['report', 'Name contains text'], ['"annual report"', 'Quote text containing spaces'], ['*.pdf', '* = any characters; ? = one character'], ['report|invoice', 'Either name']]);
  define('path', [['Projects', 'Path contains text'], ['"Projects\\Annual Reports"', 'Quote paths containing spaces'], ['*Projects*', 'Wildcard path matching']]);
  define('regex', [['^report.*\\.pdf$', 'Regular expression; ^ start, $ end, .* any text'], ['"annual.*report"', 'Quote patterns containing spaces']]);
  define('file files folders', [['report', 'Restrict kind and search this name'], ['"annual report"', 'Quote names containing spaces'], ['*', 'All entries of this kind']]);
  define('folder', [['', 'Open the folder picker; use folders: for folder-name search']]);
  const filters = Object.keys(examples).map(key => `${key}:`);
  let key = '', names = [], selected = 0, context, composing = false, timer, results = [];
  function current() {
    if (composing || document.activeElement !== input || input.selectionStart !== input.selectionEnd || input.selectionEnd !== input.value.length) return null;
    const value = input.value;
    const filter = /(?:^|[\s(])([a-z-]+):("[^"]*"?|[^\s()]*)$/i.exec(value);
    if (filter && examples[filter[1].toLowerCase()] && (value.slice(0, filter.index).match(/"/g) || []).length % 2 === 0) {
      const operator = filter[1].toLowerCase(), raw = filter[2], typed = raw.replace(/^"|"$/g, '');
      const directory = ['inside', 'foldername', 'folder-name', 'in', 'source', 'location', 'where', 'under', 'parent'].includes(operator) && /[\\/]/.test(typed);
      const folder = ['inside', 'foldername', 'folder-name', 'in'].includes(operator) && !!typed && !/[\\/:]/.test(typed);
      if (directory) {
        const split = Math.max(typed.lastIndexOf('\\'), typed.lastIndexOf('/'));
        const directoryPath = typed.slice(0, split + 1), prefix = typed.slice(split + 1);
        const quoted = raw.startsWith('"');
        return { key: `directory:${operator}:${directoryPath}:${prefix}`, typed, raw, start: value.length - raw.length, directory: true, directoryPath, prefix, operator,
          tokenStart: value.length - filter[0].length + filter[0].indexOf(filter[1]), tokenEnd: value.length, quoted };
      }
      return { key: `${folder ? 'folder' : operator}:${typed}`, typed, raw, start: value.length - raw.length, folder, operator };
    }
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
      if (context.directory) {
        const path = context.directoryPath, prefix = context.prefix;
        timer = setTimeout(() => host.post({ type: 'predictDirectory', path, prefix }), 80);
      } else if (context.folder) {
        const prefix = context.typed;
        timer = setTimeout(() => host.post({ type: 'predictFolder', prefix }), 80);
      }
    }
    if (context.directory) {
      // Directory child names are supplied asynchronously by the host.
    } else if (context.operator && !context.folder) {
      const choices = examples[context.operator];
      const matches = choices.filter(([value]) => value.toLocaleLowerCase().startsWith(context.raw.toLocaleLowerCase()));
      names = (matches.length ? matches : choices).map(([value]) => value);
    } else if (!context.folder) names = [...new Set([...filters, ...results])]
      .filter(name => name.toLocaleLowerCase().startsWith(context.typed.toLocaleLowerCase()) && name.length > context.typed.length)
      .sort((a, b) => a.length - b.length || a.localeCompare(b));
    if (!names.length) return;
    selected %= names.length;
    const name = names[selected];
    context.replacement = context.directory
      ? `${name}\\`
      : context.folder && (/[\s()|]/.test(name) || context.raw.startsWith('"')) ? `"${name}"` : name;
    const typedForSuffix = context.directory ? context.prefix : context.raw;
    const suffix = context.replacement.toLocaleLowerCase().startsWith(typedForSuffix.toLocaleLowerCase())
      ? context.replacement.slice(typedForSuffix.length) : ` → ${name}`;
    const description = context.operator && !context.folder ? examples[context.operator].find(([value]) => value === name)?.[1] : '';
    if (!suffix && names.length === 1 && !description) return;
    const target = [...mirror.querySelectorAll('[data-query-start]')].find(el => input.value.length >= +el.dataset.queryStart && input.value.length <= +el.dataset.queryEnd);
    if (!target) return;
    const walker = document.createTreeWalker(target, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode(), offset = input.value.length - +target.dataset.queryStart;
    while (node && offset > node.length) { offset -= node.length; node = walker.nextNode(); }
    if (!node) return;
    const range = document.createRange(); range.setStart(node, offset); range.collapse(true);
    ghost.style.left = `${range.getBoundingClientRect().left - mirror.getBoundingClientRect().left}px`;
    ghost.textContent = `${suffix || (name ? ` → ${name}` : '')}${description ? `  · ${description}` : ''}${names.length > 1 ? `  ‹ ${selected + 1}/${names.length} ›` : ''}`;
    ghost.title = '← previous prediction · → next prediction · Tab accept · ↑/↓ navigate results';
  }
  const onMessage = message => {
    if (message.type === 'init' && message.types) {
      const choices = [...examples.type];
      for (const type of message.types) if (!choices.some(([value]) => value === type.name)) choices.push([type.name, type.label]);
      define('type t kind', choices);
    }
    if (message.type === 'folderPrediction' && key === `folder:${message.prefix}`) {
      names = message.names || []; selected = 0; render();
    } else if (message.type === 'directoryPrediction' && context?.directory
      && context.directoryPath === message.path && context.prefix === message.prefix) {
      names = message.names || []; selected = 0; render();
    } else if (message.type === 'results') {
      results = (message.items || []).filter(item => !item.command).map(item => item.name); render();
    }
  };
  document.addEventListener('keydown', event => {
    if (event.target !== input || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || event.isComposing) return;
    render();
    if (!ghost.textContent || !context) return;
    const direction = { ArrowLeft: -1, ArrowRight: 1 }[event.key];
    if (direction && names.length > 1) {
      event.preventDefault(); event.stopImmediatePropagation();
      selected = (selected + direction + names.length) % names.length; render();
    } else if (event.key === 'Tab') {
      event.preventDefault(); event.stopImmediatePropagation();
      if (context.directory) {
        const token = input.value.slice(context.tokenStart, context.tokenEnd);
        const colon = token.indexOf(':');
        const quote = context.quoted ? '"' : '';
        const replacement = `${token.slice(0, colon + 1)}${quote}${context.directoryPath}${context.replacement}${quote}`;
        input.setRangeText(replacement, context.tokenStart, context.tokenEnd, 'end');
      } else {
        input.setRangeText(context.replacement, context.start, input.value.length, 'end');
      }
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
