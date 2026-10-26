// EasySearch interface. Talks to the Windows host over WebView2 messages; when opened in a
// plain browser it falls back to the mock host in mock.js so the UI can be worked on alone.
(() => {
  'use strict';

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const esc = text => String(text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const demo = new URLSearchParams(location.search).has('demo'); // sample data inside the real window
  const inHost = !!window.chrome?.webview; // the Windows app is around us, so real file icons are available
  const hosted = inHost && !demo;
  const host = hosted
    ? {
        post: message => window.chrome.webview.postMessage(message),
        listen: handler => window.chrome.webview.addEventListener('message', e => handler(e.data)),
      }
    : window.createMockHost();
  if (!inHost) document.documentElement.classList.add('mock'); // plain browser: draw a stand-in desktop
  const videoPreview = new VideoPreview(message => host.post(message));
  let paletteVisible = true;

  // ---- icons -----------------------------------------------------------------------

  const ICONS = {
    search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/>',
    sliders: '<path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1"/><circle cx="15" cy="6" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="18" r="2"/>',
    gear: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
    folder: '<path d="M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.93a2 2 0 0 1-1.66-.9l-.82-1.2A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13c0 1.1.9 2 2 2Z"/>',
    file: '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/>',
    document: '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4M8 13h8M8 17h5"/>',
    image: '<rect x="3" y="3" width="18" height="18" rx="3"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21"/>',
    video: '<rect x="3" y="4" width="18" height="16" rx="3"/><path d="m10 9 5 3-5 3z"/>',
    audio: '<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>',
    code: '<path d="m16 18 6-6-6-6M8 6l-6 6 6 6"/>',
    archive: '<rect x="2" y="3" width="20" height="5" rx="1.5"/><path d="M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8M10 12h4"/>',
    app: '<rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/>',
    drive: '<path d="M22 12H2M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11zM6 16h.01M10 16h.01"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    bolt: '<path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z"/>',
    chevron: '<path d="m6 9 6 6 6-6"/>',
    chevronLeft: '<path d="m15 18-6-6 6-6"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    plus: '<path d="M5 12h14M12 5v14"/>',
    open: '<path d="M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
    eye: '<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>',
    copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    terminal: '<path d="m4 17 6-6-6-6M12 19h8"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 16v-4M12 8h.01"/>',
    brush: '<path d="M12 22a10 10 0 1 1 10-10c0 2.5-2 3.5-4 3.5h-2.2a2 2 0 0 0-1.4 3.4c.8.8.3 3.1-2.4 3.1Z"/><circle cx="7.5" cy="11.5" r="1"/><circle cx="11" cy="7.5" r="1"/><circle cx="16" cy="9" r="1"/>',
    database: '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  };
  const icon = (name, size = 16) =>
    `<svg class="i" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[name] || ''}</svg>`;

  // ---- vocabulary ------------------------------------------------------------------

  const KINDS = [
    { id: 'all', label: 'All types' },
    { id: 'folder', label: 'Folders' },
    { id: 'document', label: 'Documents' },
    { id: 'image', label: 'Images' },
    { id: 'video', label: 'Video' },
    { id: 'audio', label: 'Audio' },
    { id: 'code', label: 'Code' },
    { id: 'archive', label: 'Archives' },
    { id: 'app', label: 'Apps' },
    { id: '3d', label: '3D models' },
  ];

  const DAY = 86400000;
  const startOfToday = () => new Date().setHours(0, 0, 0, 0);

  /** Choices in the Size drop-down. Each one fills the same form that typing "size:" opens. */
  const SIZE_PRESETS = [
    { id: 'empty', label: 'Empty', fields: { op: 'eq', lo: '0', loUnit: 'B' } },
    { id: 'tiny', label: 'Under 100 KB', fields: { op: 'max', lo: '100', loUnit: 'KB' } },
    { id: 'small', label: '100 KB – 1 MB', fields: { op: 'between', lo: '100', loUnit: 'KB', hi: '1', hiUnit: 'MB' } },
    { id: 'medium', label: '1 – 100 MB', fields: { op: 'between', lo: '1', loUnit: 'MB', hi: '100', hiUnit: 'MB' } },
    { id: 'large', label: '100 MB – 1 GB', fields: { op: 'between', lo: '100', loUnit: 'MB', hi: '1', hiUnit: 'GB' } },
    { id: 'huge', label: 'Over 1 GB', fields: { op: 'min', lo: '1', loUnit: 'GB' } },
  ];
  const isoOf = ms => { const d = new Date(ms); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
  /** Choices in the Modified drop-down; they fill the form that "date:" opens. */
  const DATE_PRESETS = [
    { id: 'today', label: 'Today', fields: () => ({ op: 'today' }) },
    { id: 'week', label: 'Last 7 days', fields: () => ({ op: 'last', n: '7', unit: 'd' }) },
    { id: 'month', label: 'Last 30 days', fields: () => ({ op: 'last', n: '30', unit: 'd' }) },
    { id: 'year', label: 'This year', fields: () => ({ op: 'year' }) },
    { id: 'old', label: 'Over a year ago', fields: () => ({ op: 'before', d1: isoOf(Date.now() - 365 * DAY) }) },
  ];
  const SORTS = [
    { id: 'relevance', label: 'Best match' },
    { id: 'name', label: 'Name' },
    { id: 'path', label: 'Location' },
    { id: 'size', label: 'Size', desc: true },
    { id: 'modified', label: 'Date modified', desc: true },
  ];
  const ACCENTS = ['#0a84ff', '#a550e8', '#f0457f', '#f04438', '#f28b0c', '#2aa84a', '#7c7c85'];

  const EXT_KIND = {};
  const TYPE_NAMES = {
    pdf: 'PDF document', doc: 'Word document', docx: 'Word document', xls: 'Excel workbook', xlsx: 'Excel workbook',
    ppt: 'PowerPoint presentation', pptx: 'PowerPoint presentation', txt: 'Text document', md: 'Markdown document',
    csv: 'CSV table', exe: 'Application', msi: 'Installer', lnk: 'Shortcut', zip: 'ZIP archive', json: 'JSON file',
    html: 'HTML page', svg: 'SVG image', jpg: 'JPEG image', jpeg: 'JPEG image',
  };
  const KIND_NOUN = { image: 'image', video: 'video', audio: 'audio', code: 'source file', archive: 'archive', document: 'document' };
  Object.entries({
    image: 'jpg jpeg png gif bmp webp svg ico tif tiff heic heif avif raw cr2 nef arw dng psd ai eps',
    video: 'mp4 mkv mov avi wmv webm m4v flv mpg mpeg 3gp ts mts',
    audio: 'mp3 wav flac aac ogg m4a wma opus aiff aif mid midi',
    document: 'pdf doc docx xls xlsx ppt pptx txt md rtf odt ods odp csv epub pages numbers key tex one',
    code: 'js ts jsx tsx mjs cjs py cs java c h cpp hpp cc go rs rb php html htm css scss less json xml yaml yml toml sql sh ps1 psm1 bat cmd swift kt lua r dart vue svelte ini cfg conf gradle sln slnx csproj props targets',
    archive: 'zip rar 7z tar gz bz2 xz tgz iso cab zst',
    app: 'exe msi lnk appx msix com scr',
  }).forEach(([kind, list]) => list.split(' ').forEach(ext => (EXT_KIND[ext] = kind)));

  const extOf = name => {
    const dot = name.lastIndexOf('.');
    return dot < 0 || dot === name.length - 1 ? '' : name.slice(dot + 1).toLowerCase();
  };
  const kindOf = item => (item.isDir ? 'folder' : EXT_KIND[extOf(item.name)] || 'file');
  const kindIcon = kind => (kind === 'file' ? 'file' : kind);
  const pathOf = item => (item.dir ? (item.dir.endsWith('\\') ? item.dir : item.dir + '\\') + item.name : item.name);

  function typeName(item) {
    if (item.isDir) return item.dir ? 'Folder' : 'Location';
    const ext = extOf(item.name);
    if (!ext) return 'File';
    if (TYPE_NAMES[ext]) return TYPE_NAMES[ext];
    const noun = KIND_NOUN[EXT_KIND[ext]];
    return `${ext.toUpperCase()} ${noun || 'file'}`;
  }

  // ---- formatting ------------------------------------------------------------------

  const number = new Intl.NumberFormat('en-US');
  const longDate = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  const KB = 1024;

  function formatSize(bytes) {
    if (bytes < KB) return `${bytes} B`;
    const units = ['KB', 'MB', 'GB', 'TB'];
    let value = bytes / KB, unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    const text = value < 10 ? value.toFixed(1) : value.toFixed(0);
    return `${text.replace(/\.0$/, '')} ${units[unit]}`;
  }

  function formatDate(ms) {
    const date = new Date(ms), now = new Date();
    const days = Math.round((startOfToday() - new Date(ms).setHours(0, 0, 0, 0)) / DAY);
    if (days <= 0) return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
    if (days === 1) return 'Yesterday';
    if (days < 7) return date.toLocaleDateString(undefined, { weekday: 'long' });
    const options = { day: 'numeric', month: 'short' };
    if (date.getFullYear() !== now.getFullYear()) options.year = 'numeric';
    return date.toLocaleDateString(undefined, options);
  }

  function formatAgo(ms) {
    const minutes = Math.round((Date.now() - ms) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    if (minutes < 1440) return `${Math.round(minutes / 60)} h ago`;
    return formatDate(ms);
  }

  /** "C:\Users\me\Projects\app" -> "Home › Projects › app", dropping middle folders when too long. */
  function breadcrumb(dir, max = 62) {
    if (!dir) return '';
    let parts = dir.split('\\').filter(Boolean);
    const home = state.home;
    if (home && dir.toLowerCase().startsWith(home.toLowerCase()) && (dir.length === home.length || dir[home.length] === '\\'))
      parts = ['Home', ...dir.slice(home.length).split('\\').filter(Boolean)];

    const length = list => list.reduce((sum, part) => sum + part.length + 3, 0);
    if (length(parts) > max && parts.length > 3) {
      const tail = [];
      let i = parts.length - 1;
      while (i > 0 && length([parts[0], '…', ...tail, parts[i]]) <= max) tail.unshift(parts[i--]);
      if (tail.length === 0) tail.push(parts[parts.length - 1]);
      parts = [parts[0], '…', ...tail];
    }
    return parts.map(esc).join('<i>›</i>');
  }

  /** Plain words from the query, used to embolden matches in file names. */
  function highlightTerms(query) {
    if (state.options.regex) return [];
    const terms = [];
    let negated = false, excluded = 0; // excluded: how many ( deep we are inside a NOT (...)
    for (const token of query.match(/"[^"]*"|\S+/g) || []) {
      let text = token.replace(/"/g, '');
      if (!token.includes('"')) {
        if (/^(and|or|not|&&|\|\||\|)$/i.test(text)) { negated = text === 'NOT'; continue; } // logic words, not names
        const opens = text.match(/^\(*/)[0].length, closes = text.match(/\)*$/)[0].length;
        text = text.replace(/^\(+/, '').replace(/\)+$/, '');
        if (excluded > 0) { excluded += opens - closes; if (excluded < 0) excluded = 0; continue; }
        if (negated && opens > 0) { negated = false; excluded = Math.max(0, opens - closes); continue; }
        if (!text) continue;
      }
      if (negated) { negated = false; continue; } // the word after NOT is excluded, not matched
      if (text.startsWith('!')) continue;
      if (/^(ext|type|kind|size|dm|modified|date|datemodified|changed|updated|in|source|location|inside|where|under|parent|path|regex|folders?|files?):/i.test(text)) continue;
      text = text.replace(/^name:/i, '');
      for (let part of text.split('|')) {
        if (/[*?]/.test(part)) continue;
        if (part.includes('\\') || part.includes('/')) part = part.split(/[\\/]/).pop();
        if (part) terms.push(part.toLowerCase());
      }
    }
    return terms;
  }

  function highlight(name, terms) {
    if (terms.length === 0) return esc(name);
    const lower = name.toLowerCase();
    const ranges = [];
    for (const term of terms) {
      const at = lower.indexOf(term);
      if (at >= 0) ranges.push([at, at + term.length]);
    }
    if (ranges.length === 0) return esc(name);
    ranges.sort((a, b) => a[0] - b[0]);
    let html = '', position = 0;
    for (const [start, end] of ranges) {
      if (end <= position) continue;
      const from = Math.max(start, position);
      html += `${esc(name.slice(position, from))}<mark>${esc(name.slice(from, end))}</mark>`;
      position = end;
    }
    return html + esc(name.slice(position));
  }

  // ---- state -----------------------------------------------------------------------

  const state = {
    settings: null,
    status: { indexing: false, items: 0, scanned: 0, version: 0 },
    places: [],
    home: '',
    version: '',
    view: 'main',
    settingsTab: 'general',
    recording: false,
    query: '', // the complete query: committed chips plus the text still in the box
    committed: [], // finished query terms, applied but shown as tags under the box
    editingId: 0, // the committed chip whose text is back in the box for editing
    explorerChipId: 0, // the automatic "in:" chip for the Explorer window the palette opened from
    types: [], // named types from the host: { name, label, exts, kind }
    sort: 'relevance',
    sortDirections: Object.fromEntries(SORTS.map(s => [s.id, !!s.desc])),
    options: { matchCase: false, matchPath: false, regex: false },
    limit: 150,
    searchId: 0,
    searchedVersion: 0,
    items: [], // file hits, or home commands ({ command: true, ... })
    total: 0,
    elapsed: 0,
    error: null,
    pending: false,
    selected: 0,
    previewId: 0,
  };

  const els = {
    window: $('#window'), query: $('#query'), count: $('#count'), list: $('#list'),
    preview: $('#preview'), status: $('#status'), hints: $('#hints'), settings: $('#settings'),
    menu: $('#menu'), toast: $('#toast'),
  };
  const queryHighlight = $('#query-highlight');
  const activeFilters = $('#active-filters');
  let queryComposing = false;
  let showFullDirectoryPaths = document.activeElement === els.query;
  const onPredictionMessage = window.setupPredictions(els.query, queryHighlight, host);

  // While editing, the mirror stays character-for-character aligned with the input. In the
  // blurred state, long directory filters can use a compact visual label safely.
  function queryTokens(value) {
    const tokens = [];
    for (const match of value.matchAll(/(?:[^"\s()]|"[^"]*(?:"|$))+/g)) {
      const text = match[0];
      const filter = /^(type|t|ext|kind|size|date|dm|modified|changed|updated|datemodified|notin|in|source|location|inside|foldername|where|under|parent|path|regex|folders?|files?):/i.exec(text);
      const logic = /^(AND|OR|NOT)$/i.test(text) && (text.toUpperCase() !== 'NOT' || text === 'NOT');
      if (filter || logic) tokens.push({ start: match.index, end: match.index + text.length,
        text, key: filter?.[1].toLowerCase(), logic: logic ? text.toLowerCase() : '' });
    }
    return tokens;
  }

  function syncQueryLayout() {
    const selecting = els.query.selectionStart !== els.query.selectionEnd;
    els.query.parentElement.classList.toggle('native-text', queryComposing || selecting);
    queryHighlight.scrollLeft = els.query.scrollLeft;
    if (typeof renderPrediction === 'function') renderPrediction();
  }

  function renderQueryHighlight() {
    const value = els.query.value;
    const tokens = queryTokens(value);
    const span = (text, start, end, className = '') => '<span class="' + className +
      '" data-query-start="' + start + '" data-query-end="' + end + '">' + esc(text) + '</span>';
    let html = '', at = 0;
    for (const token of tokens) {
      if (token.start > at) html += span(value.slice(at, token.start), at, token.start);
      if (token.key) {
        const colon = token.text.indexOf(':');
        let shownValue = token.text.slice(colon + 1);
        const isDirectoryFilter = /^(in|notin|source|location|where|under|parent)$/.test(token.key);
        const quote = shownValue.startsWith('"') ? 1 : 0;
        const closeQuote = shownValue.endsWith('"') ? 1 : 0;
        const path = shownValue.slice(quote, shownValue.length - closeQuote);
        const parts = [...path.matchAll(/[^\\/]+/g)];
        if (!showFullDirectoryPaths && isDirectoryFilter && shownValue.length > 52 && parts.length > 2) {
          const suffixStart = parts[parts.length - 2].index - 1;
          shownValue = shownValue.slice(0, quote) + '…' + path.slice(suffixStart) + shownValue.slice(shownValue.length - closeQuote);
        }
        const family = /^(type|t|ext|kind)$/.test(token.key) ? 'type'
          : /^(size)$/.test(token.key) ? 'size'
          : /^(date|dm|modified|changed|updated|datemodified)$/.test(token.key) ? 'date'
          : /^(in|source|location|inside|where|under|parent|path|folder)$/.test(token.key) ? 'location'
          : /^(notin)$/.test(token.key) ? 'notin'
          : /^(foldername)$/.test(token.key) ? 'foldername' : 'other';
        const label = span(token.text.slice(0, colon + 1), token.start, token.start + colon + 1, 'query-filter-label');
        const filterValue = span(shownValue, token.start + colon + 1, token.end, 'query-filter-value');
        html += '<span class="query-filter query-filter-' + family + '">' + label + filterValue + '</span>';
      } else html += span(token.text, token.start, token.end, 'logic-' + token.logic);
      at = token.end;
    }
    if (at < value.length) html += span(value.slice(at), at, value.length);
    if (queryHighlight.innerHTML !== html) queryHighlight.innerHTML = html;
    const controls = state.committed
      .filter(chip => chip.id !== state.editingId)
      .map(chipHtml)
      .join('');
    if (activeFilters.innerHTML !== controls) activeFilters.innerHTML = controls;
    syncQueryLayout();
  }

  // ---- committed query chips -------------------------------------------------------------
  // Typing fills the search box; Enter (or moving on to another term with a space) turns what
  // was typed into a chip under the box. The chip stays part of the query, while the box is
  // free for the next condition. Clicking a chip puts its text back in the box to edit it.

  const LOGIC_CHIP = /^(AND|OR|NOT|&&|\|\||\|)$/;
  const FILTER_CHIP = /^(type|t|ext|kind|size|date|dm|modified|changed|updated|datemodified|notin|in|source|location|inside|foldername|folder-name|where|under|parent|path|regex|folders?|files?):/i;

  /** Splits query text into space-separated chunks, keeping "quoted phrases" whole. */
  function splitQueryChunks(text) {
    const chunks = [];
    let current = '', quote = false;
    for (const char of String(text)) {
      if (char === '"') { quote = !quote; current += char; continue; }
      if (!quote && /\s/.test(char)) {
        if (current) chunks.push(current);
        current = '';
        continue;
      }
      current += char;
    }
    if (current) chunks.push(current);
    return { chunks, quote };
  }

  function chipFamily(text) {
    if (LOGIC_CHIP.test(text)) return 'logic';
    const filter = FILTER_CHIP.exec(text);
    if (!filter) return 'text';
    const key = filter[1].toLowerCase();
    if (/^(type|t|ext|kind)$/.test(key)) return 'type';
    if (key === 'size') return 'size';
    if (/^(date|dm|modified|changed|updated|datemodified)$/.test(key)) return 'date';
    if (key === 'notin') return 'notin';
    if (/^(in|source|location|where|under|parent)$/.test(key)) return 'location';
    if (/^(inside|foldername|folder-name)$/.test(key)) return 'foldername';
    return 'other';
  }

  function chipHtml(chip) {
    const text = chip.text, family = chipFamily(text);
    const remove = '<button type="button" class="filter-remove" data-chip-id="' + chip.id +
      '" title="Remove ' + esc(text) + '" aria-label="Remove ' + esc(text) + '">' + icon('x', 10) + '</button>';
    if (family === 'logic' || family === 'text') {
      const label = family === 'text' ? text.replace(/^"|"$/g, '') : text;
      const logicClass = family === 'logic' && /^(AND|OR|NOT)$/i.test(text) ? ' logic-' + text.toLowerCase() : '';
      return '<span class="query-filter-chip query-chip-' + family + logicClass + '" data-chip-id="' + chip.id + '">' +
        '<button type="button" class="filter-edit" data-chip-id="' + chip.id +
        '" title="Edit ' + esc(text) + '" aria-label="Edit ' + esc(text) + '">' + esc(label) + '</button>' +
        remove + '</span>';
    }

    const colon = text.indexOf(':');
    const key = text.slice(0, colon + 1), value = text.slice(colon + 1);
    const path = value.replace(/^"|"$/g, '');
    const folder = family === 'location' && path;
    const label = folder ? path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || path : value;
    const open = folder
      ? '<button type="button" class="chip-folder-open" data-folder-path="' + esc(path) +
        '" title="Open ' + esc(path) + ' in File Explorer" aria-label="Open ' + esc(path) +
        ' in File Explorer">' + icon('folder', 13) + '</button>'
      : '';
    const body = folder ? esc(label)
      : '<span class="query-filter-label">' + esc(key) + '</span><span class="query-filter-value">' + esc(label) + '</span>';
    return '<span class="query-filter-chip query-chip-' + family + '" data-chip-id="' + chip.id + '">' + open +
      '<button type="button" class="filter-edit" data-chip-id="' + chip.id +
      '" title="Edit ' + esc(text) + '" aria-label="Edit ' + esc(text) + '">' + body + '</button>' +
      remove + '</span>';
  }

  function addCommitted(text, { prepend = false } = {}) {
    text = String(text || '').trim();
    if (!text) return null;
    const chip = { id: ++chipSeq, text };
    if (prepend) state.committed.unshift(chip);
    else state.committed.push(chip);
    return chip;
  }

  /** Adds each space-separated term in text as a chip. */
  function commitText(text) {
    const { chunks } = splitQueryChunks(text);
    for (const chunk of chunks) addCommitted(chunk);
    return chunks;
  }

  /** The whole query: every committed chip plus whatever is still being typed. */
  function composeQuery() {
    const parts = state.committed.map(chip => chip.text);
    const draft = els.query.value.trim();
    if (state.editingId) {
      const index = state.committed.findIndex(chip => chip.id === state.editingId);
      if (index >= 0) {
        if (draft) parts[index] = draft;
        else parts.splice(index, 1);
      }
    } else if (draft) parts.push(draft);
    return parts.join(' ').trim();
  }

  /** Moves complete terms out of the box as they are followed by a space, leaving a clear box. */
  function autoCommitInput() {
    if (queryComposing || state.editingId) return false;
    const value = els.query.value;
    const { chunks, quote } = splitQueryChunks(value);
    const endsWithSpace = /\s$/.test(value);
    const complete = quote ? chunks.slice(0, -1) : endsWithSpace ? chunks : chunks.slice(0, -1);
    if (complete.length === 0) {
      if (endsWithSpace && chunks.length === 0) els.query.value = '';
      return false;
    }
    for (const chunk of complete) addCommitted(chunk);
    els.query.value = quote ? chunks[chunks.length - 1] : endsWithSpace ? '' : chunks[chunks.length - 1];
    const caret = els.query.value.length;
    els.query.setSelectionRange(caret, caret);
    return true;
  }

  /** Enter in a non-empty box commits its terms; an empty box opens the selection instead. */
  function commitInput() {
    if (state.editingId) { finishEdit(); return true; }
    const text = els.query.value.trim();
    if (!text) return false;
    const before = state.query;
    commitText(text);
    els.query.value = '';
    syncQuery();
    renderQueryHighlight();
    renderHints();
    renderCount();
    els.query.focus();
    if (state.query !== before) newSearch();
    return true;
  }

  /** Clicking a chip lifts its text back into the box; committing puts it in the same slot. */
  function beginEditChip(id) {
    const chip = state.committed.find(entry => entry.id === id);
    if (!chip || state.editingId === id) return;
    if (state.editingId) finishEdit();
    if (els.query.value.trim()) {
      commitText(els.query.value);
      els.query.value = '';
    }
    if (state.explorerChipId === id) state.explorerChipId = 0; // edited by hand: it is no longer automatic
    state.editingId = id;
    els.query.value = chip.text;
    const before = state.query;
    syncQuery();
    renderQueryHighlight();
    renderHints();
    els.query.focus();
    els.query.setSelectionRange(0, els.query.value.length);
    if (state.query !== before) newSearch();
  }

  function finishEdit({ cancel = false } = {}) {
    if (!state.editingId) return false;
    const before = state.query;
    const index = state.committed.findIndex(chip => chip.id === state.editingId);
    if (index >= 0 && !cancel) {
      const text = els.query.value.trim();
      const chunks = splitQueryChunks(text).chunks;
      if (chunks.length) {
        // Editing can add another condition ("type:image size:>1mb"); each term becomes its own tag.
        state.committed.splice(index, 1, ...chunks.map(chunk => ({ id: ++chipSeq, text: chunk })));
      } else state.committed.splice(index, 1);
    }
    state.editingId = 0;
    els.query.value = '';
    syncQuery();
    renderQueryHighlight();
    renderHints();
    els.query.focus();
    if (state.query !== before) newSearch();
    return true;
  }

  function removeCommittedChip(id) {
    if (state.editingId && state.editingId !== id) finishEdit();
    let index = state.committed.findIndex(chip => chip.id === id);
    if (index < 0) return;
    if (state.editingId === id) { state.editingId = 0; els.query.value = ''; }
    if (state.explorerChipId === id) state.explorerChipId = 0;
    state.committed.splice(index, 1);

    // Do not leave a bare AND/OR/NOT at the seam where the chip used to be.
    const previous = state.committed[index - 1], next = state.committed[index];
    if (previous && (previous.text === 'NOT' || /^(AND|OR|&&|\|\||\|)$/.test(previous.text))) {
      if (state.explorerChipId === previous.id) state.explorerChipId = 0;
      state.committed.splice(index - 1, 1);
    } else if (next && /^(AND|OR|&&|\|\||\|)$/.test(next.text)) {
      if (state.explorerChipId === next.id) state.explorerChipId = 0;
      state.committed.splice(index, 1);
    }

    normalizeChips();
    syncQuery();
    renderQueryHighlight();
    renderHints();
    newSearch();
    els.query.focus();
  }

  /** Drops dangling operators left behind by removals, so the remaining chips still parse. */
  function normalizeChips() {
    let changed = true;
    while (changed) {
      changed = false;
      state.committed = state.committed.filter(chip => {
        if (chip.text) return true;
        changed = true;
        return false;
      });
      const operator = chip => /^(AND|OR|&&|\|\||\|)$/.test(chip.text);
      // A leading NOT belongs to the term after it ("NOT draft"), so only a stray AND/OR goes.
      if (state.committed.length && operator(state.committed[0])) {
        if (state.explorerChipId === state.committed[0].id) state.explorerChipId = 0;
        state.committed.shift();
        changed = true;
      }
      const last = state.committed[state.committed.length - 1];
      if (last && (operator(last) || last.text === 'NOT')) {
        if (state.explorerChipId === last.id) state.explorerChipId = 0;
        state.committed.pop();
        changed = true;
      }
      for (let i = 1; i < state.committed.length; i++) {
        if (operator(state.committed[i - 1]) && operator(state.committed[i])) {
          if (state.explorerChipId === state.committed[i - 1].id) state.explorerChipId = 0;
          state.committed.splice(i - 1, 1);
          changed = true;
          break;
        }
      }
    }
  }

  /** Adds a filter token as a chip; an empty filter stays in the box so it can be filled in. */
  function insertFilterToken(token) {
    if (!token) return;
    if (state.editingId) finishEdit();
    if (els.query.value.trim()) {
      commitText(els.query.value);
      els.query.value = '';
    }
    if (token.endsWith(':')) {
      els.query.value = token;
      els.query.focus();
      els.query.setSelectionRange(token.length, token.length);
    } else addCommitted(token);
    syncQuery();
    renderQueryHighlight();
    renderHints();
    newSearch();
  }

  function clearQuery() {
    state.committed = [];
    state.editingId = 0;
    state.explorerChipId = 0;
    els.query.value = '';
    syncQuery();
    renderQueryHighlight();
    renderHints();
    newSearch();
  }

  const iconCache = new Map(); // extension -> icon URL, so one request serves every file of a type
  const PER_FILE_ICONS = new Set(['exe', 'lnk', 'ico', 'cur', 'url', 'scr', 'msi']);

  /** URL of the icon Windows itself shows for this file or folder. */
  function iconUrl(item, size) {
    if (!inHost) return null;
    const path = pathOf(item);
    const url = `https://images.easysearch.example/icon?s=${size}&d=${item.isDir ? 1 : 0}&p=${encodeURIComponent(path)}`;
    const ext = item.isDir ? '' : extOf(item.name);
    if (!ext || PER_FILE_ICONS.has(ext)) return url;
    const key = `${ext}|${size}`;
    if (!iconCache.has(key)) iconCache.set(key, url);
    return iconCache.get(key);
  }

  /** Windows thumbnail when available, with the normal file icon as the host fallback. */
  const rowImageUrl = item =>
    `https://images.easysearch.example/thumb?s=64&d=${item.isDir ? 1 : 0}&p=${encodeURIComponent(pathOf(item))}`;

  const thumbUrl = item =>
    `https://images.easysearch.example/thumb?s=512&d=${item.isDir ? 1 : 0}&p=${encodeURIComponent(pathOf(item))}&m=${item.modified}`;

  // ---- inline filter tokens --------------------------------------------------------------
  // The editor contains the complete query. Filter terms are regular query tokens with a visual
  // treatment, so operators and text can be inserted before or after any filter.

  const SIZE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'];
  const SIZE_OPS = [
    { id: 'min', label: 'at least' },
    { id: 'max', label: 'at most' },
    { id: 'between', label: 'between' },
    { id: 'eq', label: 'exactly' },
  ];
  const UNIT_SHORT = { k: 'KB', m: 'MB', g: 'GB', t: 'TB' };
  let chipSeq = 0;

  const newSizeChip = (fields = {}) => ({ id: ++chipSeq, kind: 'size', op: 'min', lo: '', loUnit: 'MB', hi: '', hiUnit: 'MB', ...fields });
  const newTypeChip = (text = '') => ({ id: ++chipSeq, kind: 'type', text });

  const DATE_OPS = [
    { id: 'last', label: 'in the last' },
    { id: 'today', label: 'today' },
    { id: 'yesterday', label: 'yesterday' },
    { id: 'week', label: 'this week' },
    { id: 'month', label: 'this month' },
    { id: 'year', label: 'this year' },
    { id: 'on', label: 'on' },
    { id: 'before', label: 'before' },
    { id: 'after', label: 'after' },
    { id: 'between', label: 'between' },
  ];
  const DATE_UNITS = [
    { id: 'h', label: 'hours' }, { id: 'd', label: 'days' }, { id: 'w', label: 'weeks' },
    { id: 'm', label: 'months' }, { id: 'y', label: 'years' },
  ];
  const newDateChip = (fields = {}) => ({ id: ++chipSeq, kind: 'date', op: 'last', n: '', unit: 'd', d1: '', d2: '', ...fields });
  const newPlaceChip = (path = '', auto = false) => ({ id: ++chipSeq, kind: 'place', path, auto });

  /** Every spelling of a filter the backend accepts, and the form it opens. */
  const FILTER_FORM = {
    size: 'size', type: 'type', t: 'type',
    date: 'date', dm: 'date', modified: 'date', changed: 'date', updated: 'date', datemodified: 'date',
    in: 'place', notin: 'place', source: 'place', location: 'place', where: 'place',
    foldername: 'foldername', inside: 'foldername',
  };
  const FILTER_PATTERN = new RegExp(`(^|\\s)(${Object.keys(FILTER_FORM).join('|')}):("[^"]*"|\\S*)`, 'gi');
  const FOLDER_PICK_TOKEN = /(^|\\s)folder:\s*$/i;

  const pad2 = n => String(n).padStart(2, '0');

  /** "2026-03-14", "2026-03" or "2026" as an ISO day (the first day, or the last with end), or null. */
  function isoDay(text, end) {
    let m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(text);
    if (m) return `${m[1]}-${pad2(m[2])}-${pad2(m[3])}`;
    m = /^(\d{4})-(\d{1,2})$/.exec(text);
    if (m) return `${m[1]}-${pad2(m[2])}-${end ? pad2(new Date(+m[1], +m[2], 0).getDate()) : '01'}`;
    m = /^(\d{4})$/.exec(text);
    return m ? `${m[1]}-${end ? '12-31' : '01-01'}` : null;
  }

  /** Form fields for a typed date value, or null when the form can't show it. */
  function dateFields(value) {
    value = value.trim().toLowerCase();
    if (!value) return {};

    const preset = { today: 'today', yesterday: 'yesterday', week: 'week', thisweek: 'week', month: 'month', thismonth: 'month', year: 'year', thisyear: 'year' }[value];
    if (preset) return { op: preset };

    let m = /^(\d{1,5})([hdwmy])$/.exec(value);
    if (m) return { op: 'last', n: m[1], unit: m[2] };

    if ((m = /^(>=|>|<=|<)(.+)$/.exec(value))) {
      const day = isoDay(m[2], m[1] === '<=');
      if (!day) return null;
      if (m[1][0] === '>') return { op: 'after', d1: day };
      return m[1] === '<' ? { op: 'before', d1: day } : { op: 'between', d1: '', d2: day };
    }

    const dots = value.indexOf('..');
    if (dots >= 0) {
      const a = value.slice(0, dots), b = value.slice(dots + 2);
      const d1 = a ? isoDay(a, false) : '', d2 = b ? isoDay(b, true) : '';
      if ((a && !d1) || (b && !d2) || (!a && !b)) return null;
      return { op: 'between', d1, d2 };
    }

    if (/^\d{4}-\d{1,2}-\d{1,2}$/.test(value)) return { op: 'on', d1: isoDay(value, false) };
    const first = isoDay(value, false);
    return first ? { op: 'between', d1: first, d2: isoDay(value, true) } : null; // a whole month or year
  }
  const normName = text => text.toLowerCase().replace(/[\s_-]/g, '');

  /** "10mb", "1.5 GB" or "500" -> { n: '10', unit: 'MB' }; null when it isn't an amount. */
  function parseAmount(text, bareUnit = 'B') {
    const m = /^(\d+(?:[.,]\d+)?)\s*([a-z]*)$/i.exec(text.trim());
    if (!m) return null;
    const word = m[2].toLowerCase();
    const unit = word ? UNIT_SHORT[word] || word.toUpperCase() : bareUnit;
    return SIZE_UNITS.includes(unit) ? { n: m[1].replace(',', '.'), unit } : null;
  }

  /** Form fields for a typed size value, or null when the form can't show it (size:tiny ...). */
  function sizeFields(value) {
    value = value.trim();
    if (!value) return {};

    const compare = /^(>=|<=|>|<|=)\s*(.+)$/.exec(value);
    if (compare) {
      const a = parseAmount(compare[2]);
      if (!a) return null;
      return { op: compare[1][0] === '>' ? 'min' : compare[1][0] === '<' ? 'max' : 'eq', lo: a.n, loUnit: a.unit };
    }

    let parts = null, borrowUnit = false;
    const dots = value.indexOf('..');
    if (dots >= 0) parts = [value.slice(0, dots), value.slice(dots + 2)];
    else if (/^[^-]+-[^-]+$/.test(value)) { parts = value.split('-'); borrowUnit = true; } // 1-10mb: the unit covers both
    if (parts) {
      const right = parts[1].trim() ? parseAmount(parts[1]) : null;
      const left = parts[0].trim() ? parseAmount(parts[0], borrowUnit && right ? right.unit : 'B') : null;
      if ((parts[1].trim() && !right) || (parts[0].trim() && !left) || (!left && !right)) return null;
      return { op: 'between', lo: left?.n ?? '', loUnit: left?.unit ?? right.unit, hi: right?.n ?? '', hiUnit: right?.unit ?? left.unit };
    }

    const a = parseAmount(value);
    return a ? { op: 'min', lo: a.n, loUnit: a.unit } : null;
  }

  function chipFromToken(key, value) {
    value = value.replace(/^"|"$/g, '');
    switch (FILTER_FORM[key]) {
      case 'size': {
        const fields = sizeFields(value);
        return fields ? newSizeChip(fields) : null;
      }
      case 'date': {
        const fields = dateFields(value);
        return fields ? newDateChip(fields) : null;
      }
      case 'place':
        return newPlaceChip(value);
      case 'foldername':
        return { kind: 'foldername', text: value };
      default:
        return newTypeChip(value.split(/[;|,]+/).filter(Boolean).join(', '));
    }
  }

  /** Logic (OR, NOT, parentheses) can't be expressed as AND-ed forms, so such queries stay text. */
  const hasLogic = text => /(^|\s)(OR|or|NOT|\|\|?)(\s|$)|[()]/.test(text);

  /** Lifts size:, type:, date: and in: tokens (and their other names) out of a piece of text into forms. */
  function pullChips(text) {
    const chips = [];
    if (hasLogic(text)) return { rest: text, chips };
    const rest = text.replace(FILTER_PATTERN, (all, lead, key, value) => {
      const chip = chipFromToken(key.toLowerCase(), value);
      if (!chip) return all;
      chips.push(chip);
      return lead;
    });
    return { rest: chips.length ? rest.replace(/\s{2,}/g, ' ').replace(/^\s+/, '') : text, chips };
  }

  const amount = text => {
    const t = String(text).trim().replace(',', '.');
    return /^\d+(\.\d+)?$/.test(t) ? t : '';
  };

  /** The words of a type form as the backend should see them: tool names are kept whole. */
  function typeWords(text) {
    const known = new Map();
    for (const t of state.types) { known.set(normName(t.name), t.name); known.set(normName(t.label), t.name); }
    const words = [];
    for (const piece of text.split(/[;|,]+/)) {
      const name = known.get(normName(piece));
      if (name && /\s/.test(piece.trim())) words.push(name); // "Cinema 4D", "3D models"
      else words.push(...piece.split(/\s+/).filter(Boolean));
    }
    return words;
  }

  function chipToken(chip) {
    if (chip.kind === 'type') {
      const words = typeWords(chip.text);
      return words.length ? `type:${words.join(',')}` : null;
    }
    if (chip.kind === 'place') {
      const path = chip.path.trim().replace(/^"|"$/g, '');
      return path ? (/\s/.test(path) ? `in:"${path}"` : `in:${path}`) : null;
    }
    if (chip.kind === 'foldername') {
      const name = chip.text.trim().replace(/^"|"$/g, '');
      return name ? (/\s/.test(name) ? `inside:"${name}"` : `inside:${name}`) : null;
    }
    if (chip.kind === 'date') {
      const iso = d => (/^\d{4}-\d{2}-\d{2}$/.test(d) ? d : '');
      switch (chip.op) {
        case 'last': return /^\d+$/.test(chip.n.trim()) ? `date:${chip.n.trim()}${chip.unit}` : null;
        case 'on': return iso(chip.d1) ? `date:${iso(chip.d1)}` : null;
        case 'before': return iso(chip.d1) ? `date:<${iso(chip.d1)}` : null;
        case 'after': return iso(chip.d1) ? `date:>${iso(chip.d1)}` : null;
        case 'between': {
          const a = iso(chip.d1), b = iso(chip.d2);
          return a && b ? `date:${a}..${b}` : a ? `date:>=${a}` : b ? `date:<=${b}` : null;
        }
        default: return `date:${chip.op}`;
      }
    }
    const lo = amount(chip.lo), hi = amount(chip.hi);
    const side = (n, unit) => n + unit.toLowerCase();
    switch (chip.op) {
      case 'between':
        if (lo && hi) return `size:${side(lo, chip.loUnit)}..${side(hi, chip.hiUnit)}`;
        if (lo) return `size:>=${side(lo, chip.loUnit)}`;
        if (hi) return `size:<=${side(hi, chip.hiUnit)}`;
        return null;
      case 'max': return lo ? `size:<=${side(lo, chip.loUnit)}` : null;
      case 'eq': return lo ? `size:=${side(lo, chip.loUnit)}` : null;
      default: return lo ? `size:>=${side(lo, chip.loUnit)}` : null;
    }
  }

  /** The complete query is the committed chips plus the text still being edited. */
  function syncQuery() {
    state.query = composeQuery();
    els.query.placeholder = state.committed.length ? 'Add another condition' : 'Search files and folders';
    renderFilters();
  }

  // ---- searching -------------------------------------------------------------------

  const hasFilters = () => /\b(?:type|t|ext|kind|size|date|dm|modified|changed|updated|in|source|location|inside|foldername):\S+/i.test(state.query);
  const hasQuery = () => state.query.trim().length > 0;

  function runSearch({ keepSelection = false } = {}) {
    const sort = SORTS.find(s => s.id === state.sort) || SORTS[0];
    state.pending = true;
    state.keepSelection = keepSelection;
    state.searchedVersion = state.status.version;
    state.searchedItems = state.status.items;
    state.searchedScanned = state.status.scanned;
    clearTimeout(refreshTimer);
    refreshTimer = 0;
    host.post({
      type: 'search',
      id: ++state.searchId,
      query: state.query.trim(),
      sort: sort.id,
      desc: !!state.sortDirections[sort.id],
      limit: state.limit,
      matchCase: state.options.matchCase,
      matchPath: state.options.matchPath,
      regex: state.options.regex,
      showHidden: state.settings.showHidden,
      showSystem: state.settings.showSystem,
    });
  }

  function newSearch() {
    clearTimeout(searchTimer);
    searchTimer = 0;
    state.limit = 150;
    runSearch();
  }

  function onResults(message) {
    if (message.id !== state.searchId) return;
    // A refresh searches the entire index again. Its early batches are incomplete rankings,
    // not changes to the displayed list: applying them removes rows only to add them back.
    if (message.partial && (state.keepSelection || !state.pending)) return;
    const previous = state.keepSelection ? selectedItem() : null;
    state.pending = message.partial === true;
    const unchanged = state.keepSelection && state.renderedQuery === state.query &&
      state.error === (message.error || null) && state.total === (message.total || 0) &&
      JSON.stringify(state.items) === JSON.stringify(message.items || []);
    if (unchanged) {
      scheduleRefresh();
      return;
    }
    state.items = message.items || [];
    state.total = message.total || 0;
    state.elapsed = message.elapsedMs || 0;
    state.error = message.error || null;

    let selected = 0;
    if (previous) {
      const path = pathOf(previous);
      selected = Math.max(0, state.items.findIndex(item => pathOf(item) === path));
      if (selected === 0 && state.selected < state.items.length && state.keepSelection === 'position') selected = state.selected;
    }
    state.selected = selected;
    renderList({ keepScroll: !!state.keepSelection });
    renderCount();
    if (!state.pending) scheduleRefresh();
  }

  // ---- home (empty query) ----------------------------------------------------------

  function recentSearches() {
    try { return JSON.parse(localStorage.getItem('recent') || '[]'); } catch { return []; }
  }

  function rememberSearch() {
    const query = state.query.trim();
    if (!query) return;
    const recent = [query, ...recentSearches().filter(q => q !== query)].slice(0, 5);
    try { localStorage.setItem('recent', JSON.stringify(recent)); } catch { /* storage unavailable */ }
  }

  function openHistoryMenu() {
    const recent = recentSearches().filter(q => typeof q === 'string' && q.trim());
    const items = recent.length
      ? [{ head: 'Recent searches' }, ...recent.map(query => ({ label: query, icon: 'search', run: () => setQuery(query) }))]
      : [{ head: 'Search history' }, { label: 'Open a result to save this search', icon: 'clock', run: () => {} }];
    if (recent.length) {
      items.push({ sep: true }, { label: 'Clear history', icon: 'x', run: () => {
        try { localStorage.removeItem('recent'); } catch { /* storage unavailable */ }
        toast('Search history cleared');
      } });
    }
    menu.open($('#btn-history'), items, { align: 'right', minWidth: 230 });
  }

  function homeCommands() {
    const place = label => state.places.find(p => p.label === label);
    const pathOfPlace = label => place(label)?.path;
    const commands = [
      { section: 'Quick filters' },
      { command: true, icon: 'document', kind: 'document', path: pathOfPlace('Documents'), title: 'Recent documents', note: 'Documents, newest first', run: () => applyPreset({ kind: 'document', sort: 'modified' }) },
      { command: true, icon: 'image', kind: 'image', path: pathOfPlace('Pictures'), title: 'Images from the last 7 days', note: 'Photos, screenshots and graphics', run: () => applyPreset({ kind: 'image', modified: 'week', sort: 'modified' }) },
      { command: true, icon: 'archive', kind: 'archive', path: state.settings.roots[0], title: 'Large files', note: 'Everything over 1 GB, biggest first', run: () => applyPreset({ size: 'huge', sort: 'size' }) },
      { command: true, icon: 'folder', kind: 'folder', path: 'folder', title: 'Recently changed folders', note: 'Where work has been happening', run: () => applyPreset({ kind: 'folder', sort: 'modified' }) },
    ];
    const downloads = place('Downloads');
    if (downloads)
      commands.push({ command: true, icon: 'clock', kind: 'accent', path: downloads.path, title: 'Latest downloads', note: breadcrumbText(downloads.path), run: () => applyPreset({ scope: downloads, sort: 'modified' }) });

    const recent = recentSearches();
    if (recent.length > 0) {
      commands.push({ section: 'Recent searches' });
      for (const query of recent)
        commands.push({ command: true, icon: 'search', kind: 'file', title: query, note: 'Search again', run: () => setQuery(query) });
    }
    return commands;
  }

  const breadcrumbText = dir => breadcrumb(dir).replace(/<i>›<\/i>/g, ' › ').replace(/&amp;/g, '&');

  function applyPreset(preset) {
    const tokens = [];
    if (preset.kind) tokens.push(chipToken(newTypeChip(preset.kind)));
    if (preset.size) tokens.push(chipToken(newSizeChip(SIZE_PRESETS.find(p => p.id === preset.size).fields)));
    if (preset.modified) tokens.push(chipToken(newDateChip(DATE_PRESETS.find(p => p.id === preset.modified).fields())));
    if (preset.scope) tokens.push(chipToken(newPlaceChip(preset.scope.path)));
    state.sort = preset.sort || state.settings.defaultSort;
    setQuery(tokens.filter(Boolean).join(' '));
  }

  function showHome() {
    state.error = null;
    state.total = 0;
    const commands = homeCommands();
    state.sections = commands;
    state.items = commands.filter(c => c.command);
    state.selected = 0;
    renderList();
    renderCount();
  }

  // ---- list ------------------------------------------------------------------------

  const selectedItem = () => state.items[state.selected] || null;

  /**
   * The icon for a row. Inside the app this is the icon Windows shows for the item; the drawn
   * glyph underneath only appears if Windows has none (and in the browser preview).
   */
  function rowIcon(item, size = 18) {
    // A quick filter borrows the icon of the folder or drive it is about.
    const target = item.command ? (item.path ? { name: item.path, dir: '', isDir: true } : null) : item;
    const kind = item.command ? item.kind : kindOf(item);
    const glyph = icon(item.command ? item.icon : kindIcon(kind), size);
    const url = target ? rowImageUrl(target) : null;
    if (!url) return `<span class="row-icon k-${kind}">${glyph}</span>`;
    return `<span class="row-icon pending k-${kind}">${glyph}<img src="${esc(url)}" alt="" loading="lazy" onload="this.parentNode.classList.add('has-img')" onerror="this.parentNode.classList.remove('pending'); this.remove()"></span>`;
  }

  function renderList({ keepScroll = false } = {}) {
    state.renderedQuery = state.query;
    const scrollTop = els.list.scrollTop;
    let html = '';

    if (state.pending && state.items.length === 0) {
      html = `<div class="empty"><h2>Searching…</h2></div>`;
    } else if (state.error) {
      html = `<div class="empty error">${icon('info', 34)}<h2>That search isn’t quite right</h2><p>${esc(state.error)}</p></div>`;
    } else if (state.items.length === 0) {
      const hidden = state.settings.showHidden ? '' : ' Hidden items are off — <span class="link" data-act="show-hidden">show them</span>.';
      const indexing = state.status.indexing ? ' The index is still being built, so more may turn up.' : '';
      const reset = hasFilters() ? ' <span class="link" data-act="reset">Clear filters</span>.' : '';
      html = hasQuery()
        ? `<div class="empty">${icon('search', 34)}<h2>No results</h2><p>Nothing matches that search.${reset}${hidden}${indexing}</p></div>`
        : `<div class="empty">${icon('search', 34)}<h2>No indexed files yet</h2><p>${indexing || 'Add a folder to the index in Settings.'}</p></div>`;
    }

    if (state.items.length > 0 && !state.error) {
      // Search results often arrive in several partial batches. Replacing the whole list for each
      // batch makes WebView2 briefly clear and reload every row (including its shell icons), which
      // looks like a flash. Reuse rows with the same path and only rebuild rows whose data changed.
      const existing = new Map([...els.list.querySelectorAll('.row[data-key]')].map(row => [row.dataset.key, row]));
      const rows = [];
      const terms = highlightTerms(state.query);
      state.items.forEach((item, index) => {
        const key = pathOf(item);
        const signature = JSON.stringify([item, terms]);
        let row = existing.get(key);
        if (row) {
          existing.delete(key);
          if (row.dataset.signature !== signature) {
            row.querySelector('.row-name').innerHTML = highlight(item.name, terms);
            row.querySelector('.row-path').textContent = item.dir;
            row.querySelector('.row-meta').innerHTML = `<b>${formatDate(item.modified)}</b><span>${formatSize(item.size)}</span>`;
            row.classList.toggle('dim', !!item.hidden);
            row.dataset.signature = signature;
          }
        } else {
          const holder = document.createElement('div');
          holder.innerHTML = `<div class="row${index === state.selected ? ' sel' : ''}${item.hidden ? ' dim' : ''}" data-i="${index}" data-key="${esc(key)}" data-signature="${esc(signature)}" role="option">
            ${rowIcon(item)}
            <span class="row-main"><span class="row-name">${highlight(item.name, terms)}</span><span class="row-path">${esc(item.dir)}</span></span>
            <span class="row-meta"><b>${formatDate(item.modified)}</b><span>${formatSize(item.size)}</span></span>
          </div>`;
          row = holder.firstElementChild;
          if (keepScroll && !window.matchMedia('(prefers-reduced-motion: reduce)').matches)
            row.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 140, easing: 'ease-out' });
        }
        row.dataset.i = index;
        row.classList.toggle('sel', index === state.selected);
        rows.push(row);
      });
      // Reorder in place. Moving the whole list through a detached fragment can make WebView2
      // repaint the content surface even though every displayed result is unchanged.
      rows.forEach((row, index) => {
        const current = els.list.children[index];
        if (current !== row) els.list.insertBefore(row, current || null);
      });
      while (els.list.children.length > rows.length) els.list.lastElementChild.remove();
    } else if (els.list.innerHTML !== html) {
      els.list.innerHTML = html;
    }
    els.list.scrollTop = keepScroll ? scrollTop : 0;
    renderPreview();
    renderHints();
  }

  function select(index, { scroll = true } = {}) {
    if (state.items.length === 0) return;
    index = Math.max(0, Math.min(state.items.length - 1, index));
    if (index === state.selected && $('.row.sel', els.list)) return;
    $('.row.sel', els.list)?.classList.remove('sel');
    state.selected = index;
    const row = $(`.row[data-i="${index}"]`, els.list);
    if (row) {
      row.classList.add('sel');
      if (scroll) {
        if (index === 0) els.list.scrollTop = 0;
        else row.scrollIntoView({ block: 'nearest' });
      }
    }
    renderPreview();
    renderHints();
  }

  function renderCount() {
    if (state.error) { els.count.textContent = ''; return; }
    if (state.pending) {
      els.count.textContent = state.total > 0 ? `${number.format(state.total)}+ results · Searching…` : 'Searching…';
      return;
    }
    const total = number.format(state.total);
    const time = state.elapsed < 10 ? state.elapsed.toFixed(1) : Math.round(state.elapsed);
    els.count.textContent = `${total} ${state.total === 1 ? 'result' : 'results'} · ${time} ms`;
  }

  function maybeLoadMore() {
    if (state.pending || state.items.length >= state.total || state.limit >= 5000) return;
    if (els.list.scrollTop + els.list.clientHeight < els.list.scrollHeight - 400) return;
    state.limit = Math.min(5000, state.limit + 350);
    runSearch({ keepSelection: true });
  }

  // ---- preview ---------------------------------------------------------------------

  const TIPS = [
    ['report 2026', 'Every word, in any order'],
    ['jpg|png', 'Either one'],
    ['!draft', 'Leave matches out — or NOT draft'],
    ['report OR invoice', 'Either word; AND is implied'],
    ['ext:pdf OR size:>1gb', 'OR works between any filters'],
    ['"annual report"', 'An exact phrase'],
    ['*.log', 'Wildcards'],
    ['type:.pdf', 'By suffix — or ext:pdf'],
    ['size:1mb-10mb', 'By size, or size:>100mb'],
    ['date:7d', 'Changed lately — also today, 2026-03'],
    ['in:C:\\Projects', 'Only inside a folder (also source:)'],
    ['src\\ main', 'A backslash matches the path'],
    ['regex:^IMG_\\d+', 'Regular expressions'],
  ];

  let previewTimer = 0;
  let renderedPreviewKey = null;

  function clearPreview() {
    videoPreview.clear();
    clearTimeout(previewTimer);
    ++state.previewId;
    renderedPreviewKey = null;
  }

  function renderPreview() {
    if (!state.settings?.showPreview || !paletteVisible || state.view !== 'main') return;
    const item = selectedItem();
    if (!item || item.command) {
      if (renderedPreviewKey === 'tips') return;
      clearPreview();
      renderedPreviewKey = 'tips';
      els.preview.innerHTML = `
        <div class="tips-title">Search like a pro</div>
        <div class="tips-sub">Click an example to try it.</div>
        <div class="tips">${TIPS.map(([code, text]) => `<code data-tip="${esc(code)}">${esc(code)}</code><span>${esc(text)}</span>`).join('')}</div>`;
      return;
    }

    const path = pathOf(item);
    const previewKey = `${path}|${item.modified}|${item.size}`;
    if (renderedPreviewKey === previewKey) return;
    clearPreview();
    renderedPreviewKey = previewKey;
    const kind = kindOf(item);
    const visual = kind === 'image' || kind === 'video' || extOf(item.name) === 'pdf';
    const hero = inHost
      ? `<img class="${visual ? '' : 'icon'}" src="${esc(visual ? thumbUrl(item) : iconUrl(item, 256))}" alt="" onerror="this.replaceWith(this.nextElementSibling.content.cloneNode(true))"><template>${rowIcon({ ...item, command: false }, 34).replace(/<img[^>]*>/, '').replace(' pending', '')}</template>`
      : `<span class="row-icon k-${kind}">${icon(kindIcon(kind), 34)}</span>`;

    els.preview.innerHTML = `
      <div class="pv-hero">${hero}</div>
      <div class="pv-name">${esc(item.name)}</div>
      <div class="pv-kind">${esc(typeName(item))}${item.isDir ? '' : ' · ' + formatSize(item.size)}</div>
      <dl class="pv-meta">
        <dt>Where</dt><dd class="path">${esc(item.dir || path)}</dd>
        <dt>Modified</dt><dd>${esc(longDate.format(new Date(item.modified)))}</dd>
        <dt>${item.isDir ? 'Indexed size' : 'Size'}</dt><dd>${formatSize(item.size)}${item.size >= KB ? ` (${number.format(item.size)} bytes)` : ''}</dd>
        ${item.hidden ? '<dt>Attributes</dt><dd>Hidden</dd>' : ''}
      </dl>
      <pre class="pv-text" id="pv-text" hidden></pre>`;

    if (hosted && paletteVisible && state.view === 'main' && kind === 'video')
      videoPreview.select(path, els.preview.querySelector('.pv-hero'));

    clearTimeout(previewTimer);
    if (!item.isDir && item.size > 0 && (kind === 'code' || kind === 'file' || ['txt', 'md', 'csv', 'log'].includes(extOf(item.name)))) {
      const id = ++state.previewId;
      previewTimer = setTimeout(() => host.post({ type: 'preview', id, path }), 90);
    }
  }

  function onPreviewText(message) {
    const target = $('#pv-text');
    if (message.id !== state.previewId || !target || !message.text) return;
    target.textContent = message.text.slice(0, 1800);
    target.hidden = false;
  }

  // ---- status bar ------------------------------------------------------------------

  function renderStatus() {
    const s = state.status;
    els.status.classList.toggle('busy', s.indexing);
    const text = s.indexing
      ? `Indexing ${number.format(Math.max(s.scanned, s.items))}/${number.format(Math.max(s.scanned, s.items))} items`
      : `${number.format(s.items)} items indexed`;
    if (els.status.lastElementChild?.textContent !== text)
      els.status.innerHTML = `<span class="dot"></span><span>${text}</span>`;
    if (state.view === 'settings' && state.settingsTab === 'index') {
      const target = $('#index-status');
      if (target) target.textContent = indexSummary();
    }
  }

  const keys = (...names) => names.map(n => `<kbd>${n}</kbd>`).join('');
  const hint = (label, ...names) => `<span class="hint">${label} ${keys(...names)}</span>`;

  function renderHints() {
    const item = selectedItem();
    const typing = !!els.query.value.trim() || !!state.editingId;
    if (typing) {
      els.hints.innerHTML = hint('Tag', '↵') + hint('Reveal', 'Ctrl', '↵') + hint('Close', 'Esc');
      return;
    }
    if (!item) els.hints.innerHTML = hint('Filters', 'Tab') + hint('Close', 'Esc');
    else if (item.command) els.hints.innerHTML = hint('Run', '↵') + hint('Filters', 'Tab') + hint('Settings', 'Ctrl', ',');
    else els.hints.innerHTML = hint('Open', '↵') + hint('Reveal', 'Ctrl', '↵') + hint('Actions', 'Ctrl', 'K');
  }

  // ---- filters ---------------------------------------------------------------------

  const folderName = path => path.replace(/\\+$/, '').split('\\').pop() || path;
  function renderFilters() {
    const o = state.options;
    $('#btn-options').classList.toggle('active', o.matchCase || o.matchPath || o.regex || state.sort !== 'relevance');
  }

  /** Adds a filter token at the editor caret; repeated filters are independent AND-ed terms. */
  function setFilter(kind, fields) {
    const blank = kind === 'size' ? newSizeChip(fields)
      : kind === 'date' ? newDateChip(fields)
      : kind === 'place' ? newPlaceChip(fields.path, !!fields.auto)
      : newTypeChip(fields.text);
    const token = chipToken(blank);
    if (token) insertFilterToken(token);
    return token;
  }

  function resetFilters() {
    if (state.editingId) { state.editingId = 0; els.query.value = ''; }
    state.committed = state.committed.filter(chip => !FILTER_CHIP.test(chip.text));
    normalizeChips();
    els.query.value = els.query.value.replace(FILTER_PATTERN, '$1')
      .replace(/(^|\s)(?:AND|OR|NOT)(?=\s|$)/gi, '$1').replace(/[()]/g, ' ').replace(/\s+/g, ' ').trim();
    syncQuery(); renderQueryHighlight(); renderHints(); newSearch();
  }

  /** Replaces the whole query with chips, as history, presets and "search here" all do. */
  function setQuery(text) {
    state.committed = [];
    state.editingId = 0;
    state.explorerChipId = 0;
    els.query.value = '';
    commitText(text);
    syncQuery();
    renderQueryHighlight();
    renderHints();
    els.query.focus();
    newSearch();
    return state.committed;
  }

  function openAddFilters() {
    menu.open($('#btn-add-filter'), [
      { label: 'File type', sub: '›', run: () => openFilterSubmenu('type') },
      { label: 'Size', sub: '›', run: () => openFilterSubmenu('size') },
      { label: 'Date modified', sub: '›', run: () => openFilterSubmenu('date') },
      { label: 'Folder', sub: 'choose exact directory', run: () => host.post({ type: 'pickFolder', purpose: 'addFilterPlace' }) },
      { label: 'Inside', sub: 'folder name', run: () => insertFilterToken('inside:') },
    ]);
  }

  function openFilterSubmenu(kind) {
    let items;
    if (kind === 'type') {
      items = KINDS.filter(k => k.id !== 'all').map(k => ({ label: k.label, run: () => insertFilterToken(`type:${k.id}`) }));
      items.push({ sep: true }, { label: 'Custom file type…', sub: 'name or suffix', run: () => insertFilterToken('type:') });
    } else if (kind === 'size') {
      items = SIZE_PRESETS.map(p => ({ label: p.label, run: () => setFilter('size', p.fields) }));
      items.push({ sep: true }, { label: 'Custom size…', sub: 'e.g. >500mb', run: () => insertFilterToken('size:') });
    } else if (kind === 'date') {
      items = DATE_PRESETS.map(p => ({ label: p.label, run: () => setFilter('date', p.fields()) }));
      items.push({ sep: true }, { label: 'Custom date…', sub: 'e.g. >2026-01-01', run: () => insertFilterToken('date:') });
    } else {
      items = [];
      if (state.places.length) items.push({ head: 'Places' });
      for (const place of state.places) items.push({ label: place.label, run: () => setFilter('place', { path: place.path }) });
      if (state.settings.roots.length) items.push({ head: 'Indexed locations' });
      for (const root of state.settings.roots) items.push({ label: root, run: () => setFilter('place', { path: root }) });
      items.push({ sep: true },
        { label: 'Type folder path…', sub: 'C:\\Projects', run: () => insertFilterToken('in:') });
    }
    const title = ({ type: 'File type', t: 'File type', size: 'Size', date: 'Date modified', place: 'Folder' })[kind];
    items.unshift({ label: '‹ All filters', run: openAddFilters }, { head: title });
    menu.open($('#btn-add-filter'), items, { minWidth: 190 });
  }

  function openOptionsMenu() {
    const toggle = key => () => {
      state.options[key] = !state.options[key];
      renderFilters();
      newSearch();
    };
    const setting = key => () => { state.settings[key] = !state.settings[key]; saveSettings(); newSearch(); };
    menu.open($('#btn-options'), [
      { head: 'Sort by' },
      ...SORTS.map(s => ({
        label: s.label,
        checked: s.id === state.sort,
        tail: {
          symbol: state.sortDirections[s.id] ? '↓' : '↑',
          label: `Sort ${s.label} ${state.sortDirections[s.id] ? 'ascending' : 'descending'}`,
          run: () => {
            state.sort = s.id;
            state.sortDirections[s.id] = !state.sortDirections[s.id];
            renderFilters();
            newSearch();
            openOptionsMenu();
          },
        },
        run: () => { state.sort = s.id; renderFilters(); newSearch(); },
      })),
      { sep: true },
      { head: 'Matching' },
      { label: 'Match case', checked: state.options.matchCase, run: toggle('matchCase') },
      { label: 'Match full path', checked: state.options.matchPath, run: toggle('matchPath') },
      { label: 'Regular expression', checked: state.options.regex, run: toggle('regex') },
      { sep: true },
      { head: 'Show' },
      { label: 'Hidden items', checked: state.settings.showHidden, run: setting('showHidden') },
      { label: 'System files', checked: state.settings.showSystem, run: setting('showSystem') },
      { label: 'Preview pane', checked: state.settings.showPreview, run: () => { state.settings.showPreview = !state.settings.showPreview; saveSettings(); applySettings(); renderPreview(); } },
    ], { align: 'right' });
  }

  // ---- actions ---------------------------------------------------------------------

  function act(action, item = selectedItem()) {
    if (!item || item.command) return;
    if (action === 'open' || action === 'reveal') rememberSearch();
    if (action === 'searchHere') {
      const path = item.isDir ? pathOf(item) : item.dir;
      setQuery(chipToken(newPlaceChip(path)));
      return;
    }
    host.post({ type: 'action', action, path: pathOf(item) });
  }

  function activate() {
    const item = selectedItem();
    if (!item) return;
    if (item.command) item.run();
    else act('open');
  }

  function openActionsMenu(point) {
    const item = selectedItem();
    if (!item || item.command) return;
    const items = [
      { label: 'Open', icon: 'open', keys: ['↵'], run: () => act('open') },
      { label: 'Reveal in Explorer', icon: 'eye', keys: ['Ctrl', '↵'], run: () => act('reveal') },
      { label: item.isDir ? 'Search in this folder' : 'Search in its folder', icon: 'search', run: () => act('searchHere') },
      { sep: true },
      { label: 'Copy path', icon: 'copy', keys: ['Ctrl', 'C'], run: () => act('copyPath') },
      { label: 'Copy name', icon: 'copy', keys: ['Ctrl', '⇧', 'C'], run: () => act('copyName') },
      { label: item.isDir ? 'Copy folder' : 'Copy file', icon: 'copy', run: () => act('copyFile') },
      { sep: true },
      { label: 'Open in Terminal', icon: 'terminal', run: () => act('terminal') },
      { label: 'Properties', icon: 'info', run: () => act('properties') },
    ];
    menu.open(point || $('.row.sel', els.list) || els.list, items, { align: 'right', corner: !point });
  }

  // ---- menu ------------------------------------------------------------------------

  const menu = {
    items: [],
    index: -1,
    anchor: null,
    get isOpen() { return !els.menu.hidden; },

    open(anchor, items, { align = 'left', corner = false, minWidth = 0 } = {}) {
      this.close();
      this.items = items;
      this.anchor = anchor instanceof Element ? anchor : null;
      this.anchor?.classList.add('open');
      const actionable = items.filter(i => !i.head && !i.sep);
      this.index = Math.max(0, actionable.findIndex(i => i.checked === true));

      let n = 0;
      els.menu.innerHTML = items.map(item => {
        if (item.head) return `<div class="menu-head">${esc(item.head)}</div>`;
        if (item.sep) return '<div class="menu-sep"></div>';
        const lead = item.icon ? icon(item.icon, 15) : `<span class="check">${item.checked ? icon('check', 14) : ''}</span>`;
        const tail = item.keys ? `<span class="keys">${keys(...item.keys)}</span>` : item.sub ? `<span class="sub">${esc(item.sub)}</span>` : '';
        const index = n++;
        if (item.tail) return `<div class="menu-item menu-item-split" data-m="${index}" role="option" aria-selected="${item.checked === true}">${lead}<span class="label">${esc(item.label)}</span><button type="button" class="menu-item-tail" data-tail="${index}" title="${esc(item.tail.label)}" aria-label="${esc(item.tail.label)}">${esc(item.tail.symbol)}</button></div>`;
        return `<button class="menu-item" data-m="${index}">${lead}<span class="label">${esc(item.label)}</span>${tail}</button>`;
      }).join('');
      this.actionable = actionable;
      els.menu.style.minWidth = minWidth ? `${minWidth}px` : '';
      els.menu.hidden = false;
      this.mark();

      // Place it under the anchor (or at the pointer), flipping up when there is no room.
      const frame = els.window.getBoundingClientRect();
      const box = anchor instanceof Element ? anchor.getBoundingClientRect() : { left: anchor.x, right: anchor.x, top: anchor.y, bottom: anchor.y };
      const width = els.menu.offsetWidth, height = els.menu.offsetHeight;
      let left = corner ? frame.right - width - 14 : align === 'right' ? box.right - width : box.left;
      let top = corner ? frame.bottom - height - 46 : box.bottom + 6;
      if (top + height > frame.bottom - 8) top = Math.max(frame.top + 8, box.top - height - 6);
      left = Math.max(frame.left + 8, Math.min(left, frame.right - width - 8));
      els.menu.style.left = `${left - frame.left}px`;
      els.menu.style.top = `${top - frame.top}px`;
    },

    close() {
      if (!this.isOpen) return;
      els.menu.hidden = true;
      this.anchor?.classList.remove('open');
      this.anchor = null;
      els.query.focus();
      scheduleRefresh();
    },

    mark() {
      $$('.menu-item', els.menu).forEach((el, i) => el.classList.toggle('sel', i === this.index));
      $(`.menu-item[data-m="${this.index}"]`, els.menu)?.scrollIntoView({ block: 'nearest' });
    },

    choose(index) {
      const item = this.actionable[index];
      this.close();
      item?.run();
    },

    key(e) {
      const count = this.actionable.length;
      if (e.key === 'ArrowDown') this.index = (this.index + 1) % count;
      else if (e.key === 'ArrowUp') this.index = (this.index - 1 + count) % count;
      else if (e.key === 'Enter') return this.choose(this.index);
      else if (e.key === 'Escape' || e.key === 'Tab') return this.close();
      else return;
      this.mark();
    },
  };

  // ---- settings --------------------------------------------------------------------

  const TABS = [
    { id: 'general', label: 'General', icon: 'gear' },
    { id: 'appearance', label: 'Appearance', icon: 'brush' },
    { id: 'index', label: 'Index', icon: 'database' },
    { id: 'search', label: 'Search', icon: 'search' },
    { id: 'about', label: 'About', icon: 'info' },
  ];

  function saveSettings() {
    host.post({ type: 'setSettings', settings: state.settings });
  }

  function applySettings() {
    const s = state.settings;
    document.documentElement.style.setProperty('--accent', s.accent);
    els.window.classList.toggle('no-preview', !s.showPreview);
    if (!s.showPreview) clearPreview();
  }

  function openSettings(tab) {
    clearPreview();
    menu.close();
    if (tab) state.settingsTab = tab;
    state.view = 'settings';
    els.settings.hidden = false;
    els.window.classList.add('in-settings');
    renderSettings();
  }

  function closeSettings() {
    stopRecording(false);
    state.view = 'main';
    els.settings.hidden = true;
    els.window.classList.remove('in-settings');
    els.query.focus();
    renderFilters();
    runSearch({ keepSelection: true });
  }

  const field = (label, note, control) => `
    <div class="field"><div class="field-text"><div class="field-label">${label}</div>${note ? `<div class="field-note">${note}</div>` : ''}</div>${control}</div>`;
  const toggle = key => `<button class="switch${state.settings[key] ? ' on' : ''}" data-toggle="${key}" role="switch" aria-checked="${!!state.settings[key]}"></button>`;
  const hotkeyKeys = text => keys(...text.split('+').map(k => (k === 'Win' ? '⊞' : k)));

  function indexSummary() {
    const s = state.status;
    if (s.indexing) return `Indexing… ${number.format(Math.max(s.scanned, s.items))} items so far`;
    const parts = [`${number.format(s.items)} items`];
    if (s.durationMs) parts.push(`last scan took ${(s.durationMs / 1000).toFixed(1)} s`);
    if (s.lastIndexed) parts.push(`updated ${formatAgo(s.lastIndexed)}`);
    return parts.join(' · ');
  }

  function settingsPane() {
    const s = state.settings;
    switch (state.settingsTab) {
      case 'general':
        return `
          <div class="group"><h2 class="group-title">Shortcut</h2>
            <div class="card">${field('Show EasySearch', 'Works from anywhere in Windows. Press it again to dismiss.',
              `<button class="recorder${state.recording ? ' recording' : ''}" id="recorder">${state.recording ? 'Type a shortcut…' : hotkeyKeys(s.hotkey)}</button>`)}</div>
          </div>
          <div class="group"><h2 class="group-title">Behaviour</h2>
            <div class="card">
              ${field('Launch at login', 'Starts quietly in the notification area.', toggle('launchAtLogin'))}
              ${field('Hide when another window is clicked', 'Keeps it out of the way, like Spotlight.', toggle('hideOnBlur'))}
            </div>
          </div>`;

      case 'appearance':
        return `
          <div class="group"><h2 class="group-title">Look</h2>
            <div class="card">
              ${field('Theme', '', `<div class="segmented">${['system', 'light', 'dark'].map(t =>
                `<button class="${s.theme === t ? 'on' : ''}" data-theme-choice="${t}">${t[0].toUpperCase() + t.slice(1)}</button>`).join('')}</div>`)}
              ${field('Accent colour', '', `<div class="swatches">${ACCENTS.map(c =>
                `<button class="swatch${s.accent.toLowerCase() === c ? ' on' : ''}" style="--c:${c}" data-accent="${c}" aria-label="Accent ${c}"></button>`).join('')}</div>`)}
              ${field('Preview pane', 'Details and a thumbnail for the selected item.', toggle('showPreview'))}
            </div>
          </div>`;

      case 'index':
        return `
          <div class="group"><h2 class="group-title">Locations</h2>
            <p class="group-note">Drives and folders that are kept indexed. Everything inside them is searchable.</p>
            <div class="card">
              ${s.roots.map((root, i) => field(esc(root), '', `<button class="icon-btn" data-remove-root="${i}" title="Remove"${s.roots.length === 1 ? ' disabled style="opacity:.3"' : ''}>${icon('x', 15)}</button>`)
                .replace('<div class="field-text">', `${rowIcon({ command: true, kind: 'folder', icon: /^[a-z]:\\?$/i.test(root) ? 'drive' : 'folder', path: root }, 15)}<div class="field-text">`)).join('')}
              <div class="field"><button class="btn ghost" id="add-root">${'Add folder…'}</button></div>
            </div>
          </div>
          <div class="group"><h2 class="group-title">Excluded folders</h2>
            <p class="group-note">A name such as <b>node_modules</b> is skipped wherever it appears. A full path skips just that folder.</p>
            <div class="card">
              <div class="tags">${s.excluded.map((entry, i) =>
                `<span class="tag"><span>${esc(entry)}</span><button data-remove-excluded="${i}" title="Remove">${icon('x', 12)}</button></span>`).join('') || '<span class="field-note">Nothing is excluded.</span>'}</div>
              <div class="field">
                <input class="text-input" id="exclude-input" placeholder="Folder name or full path" spellcheck="false">
                <button class="btn" id="exclude-add">Add</button>
                <button class="btn" id="exclude-browse">Browse…</button>
              </div>
            </div>
          </div>
          <div class="group"><h2 class="group-title">Status</h2>
            <div class="card">${field('<span id="index-status">' + esc(indexSummary()) + '</span>',
              'The index updates itself as files change. Rebuild it if something looks out of date.',
              '<button class="btn" id="rebuild">Rebuild index</button>')}</div>
          </div>`;

      case 'search':
        return `
          <div class="group"><h2 class="group-title">Results</h2>
            <div class="card">
              ${field('Show hidden items', 'Hidden files and folders, and everything inside hidden folders such as AppData.', toggle('showHidden'))}
              ${field('Show system files', 'Protected operating system files.', toggle('showSystem'))}
              ${field('Search the folder you are in', 'Opened from a File Explorer window, EasySearch searches that folder. From anywhere else it searches everywhere.', toggle('scopeToExplorer'))}
              ${field('Sort results by', 'Used whenever the palette opens.', `<select class="text-input" id="default-sort">${SORTS.map(o =>
                `<option value="${o.id}"${o.id === s.defaultSort ? ' selected' : ''}>${o.label}</option>`).join('')}</select>`)}
            </div>
          </div>`;

      default:
        return `
          <div class="group"><div class="card">
            <div class="about"><div class="about-logo">${icon('search', 28)}</div>
              <div><div class="group-title" style="margin:0">EasySearch ${esc(state.version)}</div>
              <div class="field-note">Instant file and folder search for Windows. Prototype build.</div></div></div>
            ${field('Quit EasySearch', 'Stops indexing and removes the shortcut until you start it again.', '<button class="btn danger" id="quit">Quit</button>')}
          </div></div>`;
    }
  }

  function renderSettings() {
    els.settings.innerHTML = `
      <header class="set-head">
        <button class="icon-btn" id="set-back" title="Back (Esc)">${icon('chevronLeft', 18)}</button>
        <h1>Settings</h1><span class="spacer"></span><kbd>Esc</kbd>
      </header>
      <div class="set-body">
        <nav class="set-nav">${TABS.map(t =>
          `<button class="set-tab${t.id === state.settingsTab ? ' on' : ''}" data-tab="${t.id}">${icon(t.icon, 15)}${t.label}</button>`).join('')}</nav>
        <div class="set-pane">${settingsPane()}</div>
      </div>`;
  }

  function addExclusion(value) {
    value = value.trim().replace(/^"|"$/g, '');
    if (!value || state.settings.excluded.some(e => e.toLowerCase() === value.toLowerCase())) return;
    state.settings.excluded.push(value);
    saveSettings();
    renderSettings();
  }

  function stopRecording(save, combo) {
    if (!state.recording) return;
    state.recording = false;
    if (save) {
      state.settings.hotkey = combo;
      saveSettings(); // the host registers the new shortcut
    } else {
      host.post({ type: 'suspendHotkey', on: false });
    }
    if (state.view === 'settings') renderSettings();
  }

  function recordKey(e) {
    e.preventDefault();
    e.stopPropagation();
    if (e.key === 'Escape') return stopRecording(false);
    if (['Control', 'Shift', 'Alt', 'Meta'].includes(e.key)) return;

    let key = null;
    if (/^Key[A-Z]$/.test(e.code)) key = e.code.slice(3);
    else if (/^Digit\d$/.test(e.code)) key = e.code.slice(5);
    else if (/^F\d{1,2}$/.test(e.code)) key = e.code;
    else if (e.code === 'Space') key = 'Space';

    const modifiers = [e.ctrlKey && 'Ctrl', e.altKey && 'Alt', e.shiftKey && 'Shift', e.metaKey && 'Win'].filter(Boolean);
    if (!key || modifiers.length === 0) return toast('Use Ctrl, Alt, Shift or Win with a letter, number, Space or F-key', true);
    stopRecording(true, [...modifiers, key].join('+'));
  }

  els.settings.addEventListener('click', e => {
    const target = e.target.closest('button');
    if (!target) return;
    const s = state.settings;
    const d = target.dataset;

    if (target.id === 'set-back') return closeSettings();
    if (d.tab) { stopRecording(false); state.settingsTab = d.tab; return renderSettings(); }
    if (target.id === 'recorder') {
      state.recording = true;
      host.post({ type: 'suspendHotkey', on: true });
      return renderSettings();
    }
    if (target.id === 'add-root') return host.post({ type: 'pickFolder', purpose: 'root' });
    if (target.id === 'exclude-browse') return host.post({ type: 'pickFolder', purpose: 'exclude' });
    if (target.id === 'exclude-add') return addExclusion($('#exclude-input').value);
    if (target.id === 'rebuild') { host.post({ type: 'reindex' }); return toast('Rebuilding the index…'); }
    if (target.id === 'quit') return host.post({ type: 'quit' });

    if (d.toggle) s[d.toggle] = !s[d.toggle];
    else if (d.themeChoice) s.theme = d.themeChoice;
    else if (d.accent) s.accent = d.accent;
    else if (d.removeRoot !== undefined) { if (s.roots.length > 1) s.roots.splice(+d.removeRoot, 1); }
    else if (d.removeExcluded !== undefined) s.excluded.splice(+d.removeExcluded, 1);
    else return;

    saveSettings();
    applySettings();
    renderSettings();
  });

  els.settings.addEventListener('change', e => {
    if (e.target.id !== 'default-sort') return;
    state.settings.defaultSort = state.sort = e.target.value;
    saveSettings();
  });

  els.settings.addEventListener('keydown', e => {
    if (e.target.id === 'exclude-input' && e.key === 'Enter') addExclusion(e.target.value);
  });

  // ---- toast -----------------------------------------------------------------------

  let toastTimer = 0;
  function toast(text, error = false) {
    els.toast.textContent = text;
    els.toast.classList.toggle('error', error);
    els.toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => els.toast.classList.remove('show'), error ? 3200 : 1500);
  }

  // ---- input -----------------------------------------------------------------------

  let searchTimer = 0;
  els.query.addEventListener('input', () => {
    showFullDirectoryPaths = true;
    const folderPick = FOLDER_PICK_TOKEN.exec(els.query.value);
    if (folderPick) {
      state.folderPickQuery = els.query.value.slice(0, folderPick.index).trimEnd();
      host.post({ type: 'pickFolder', purpose: 'addFilterPlace' });
      return;
    }
    const before = state.query;
    autoCommitInput();
    renderQueryHighlight();
    syncQuery();
    renderHints();
    // Finishing a term by typing a space only moves it into a chip; the search itself has not
    // changed, so leave the current results (and any search still running) alone.
    if (state.query === before) return;
    // Stop the previous query immediately, then wait for a short pause before scanning again.
    // This avoids starting a full index scan for every character while typing.
    clearTimeout(searchTimer);
    state.searchId++;
    host.post({ type: 'cancelSearch' });
    state.items = [];
    state.total = 0;
    state.error = null;
    state.selected = 0;
    state.pending = true;
    renderList();
    renderCount();
    searchTimer = setTimeout(() => {
      searchTimer = 0;
      newSearch();
    }, 120);
  });
  els.query.addEventListener('scroll', syncQueryLayout);
  els.query.addEventListener('focus', () => {
    showFullDirectoryPaths = true;
    renderQueryHighlight();
    requestAnimationFrame(syncQueryLayout);
  });
  els.query.addEventListener('blur', () => {
    showFullDirectoryPaths = false;
    els.query.scrollLeft = 0;
    renderQueryHighlight();
    if (typeof clearPrediction === 'function') clearPrediction();
  });
  els.query.addEventListener('select', syncQueryLayout);
  document.addEventListener('selectionchange', () => {
    if (document.activeElement === els.query) syncQueryLayout();
  });
  els.query.addEventListener('compositionstart', () => { queryComposing = true; syncQueryLayout(); });
  els.query.addEventListener('compositionend', () => { queryComposing = false; renderQueryHighlight(); });
  new ResizeObserver(syncQueryLayout).observe(els.query);
  renderQueryHighlight();

  $('#btn-add-filter').addEventListener('click', openAddFilters);
  $('#btn-history').addEventListener('click', openHistoryMenu);
  $('#btn-options').addEventListener('click', openOptionsMenu);
  $('#btn-settings').addEventListener('click', () => openSettings());

  els.list.addEventListener('click', e => {
    const link = e.target.closest('[data-act]');
    if (link?.dataset.act === 'reset') return resetFilters();
    if (link?.dataset.act === 'show-hidden') { state.settings.showHidden = true; saveSettings(); return newSearch(); }
    const row = e.target.closest('.row');
    if (!row) return;
    select(+row.dataset.i, { scroll: false });
    if (selectedItem()?.command) activate();
  });
  els.list.addEventListener('dblclick', e => { if (e.target.closest('.row')) activate(); });
  els.list.addEventListener('contextmenu', e => {
    e.preventDefault();
    const row = e.target.closest('.row');
    if (!row) return;
    select(+row.dataset.i, { scroll: false });
    openActionsMenu({ x: e.clientX, y: e.clientY });
  });
  els.list.addEventListener('scroll', maybeLoadMore, { passive: true });

  els.preview.addEventListener('click', e => {
    const tip = e.target.closest('[data-tip]');
    if (tip) setQuery(tip.dataset.tip);
  });

  els.menu.addEventListener('click', e => {
    const tail = e.target.closest('.menu-item-tail');
    if (tail) {
      e.preventDefault();
      e.stopPropagation();
      menu.actionable[+tail.dataset.tail]?.tail?.run();
      return;
    }
    const item = e.target.closest('.menu-item');
    if (item) menu.choose(+item.dataset.m);
  });
  els.menu.addEventListener('mousemove', e => {
    const item = e.target.closest('.menu-item');
    if (item && +item.dataset.m !== menu.index) { menu.index = +item.dataset.m; menu.mark(); }
  });

  // Keep typing focus in the search box: clicks elsewhere in the main view must not steal it.
  document.addEventListener('mousedown', e => {
    if (menu.isOpen && !e.target.closest('#menu')) {
      const reopen = menu.anchor && menu.anchor.contains(e.target);
      menu.close();
      if (reopen) { e.preventDefault(); e.stopPropagation(); menu.swallow = true; }
    }
    if (state.view === 'main' && e.target !== els.query && !e.target.closest('.preview, .qchip')) e.preventDefault();

    // Empty parts of the top bar act as a title bar: dragging them moves the window.
    if (e.button === 0 && e.target.closest('.searchbar, .set-head') && !e.target.closest('input, button')) host.post({ type: 'drag' });
  }, true);
  document.addEventListener('click', e => {
    const remove = e.target.closest('.filter-remove');
    if (remove) {
      e.preventDefault();
      e.stopPropagation();
      removeCommittedChip(Number(remove.dataset.chipId));
      return;
    }
    const folder = e.target.closest('.chip-folder-open');
    if (folder) {
      e.preventDefault();
      e.stopPropagation();
      host.post({ type: 'action', action: 'open', path: folder.dataset.folderPath });
      return;
    }
    const edit = e.target.closest('.filter-edit');
    if (edit) {
      e.preventDefault();
      e.stopPropagation();
      beginEditChip(Number(edit.dataset.chipId));
      return;
    }
    if (menu.swallow) { menu.swallow = false; e.preventDefault(); e.stopPropagation(); }
  }, true);
  document.addEventListener('contextmenu', e => e.preventDefault());

  document.addEventListener('keydown', e => {
    if (e.isComposing || queryComposing || e.keyCode === 229) return;
    if (state.recording) return recordKey(e);
    if (menu.isOpen) {
      if (['ArrowDown', 'ArrowUp', 'Enter', 'Escape', 'Tab'].includes(e.key)) { e.preventDefault(); e.stopPropagation(); menu.key(e); }
      return;
    }
    const ctrl = e.ctrlKey || e.metaKey;
    const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;

    if (state.view === 'settings') {
      if (key === 'Escape' || (ctrl && key === ',')) { e.preventDefault(); closeSettings(); }
      return;
    }

    // Typing always goes to the search box, even after clicking around in the preview.
    if (document.activeElement !== els.query && !ctrl && !e.altKey && e.key.length === 1) els.query.focus();

    switch (key) {
      case 'ArrowDown': select(state.selected + 1); break;
      case 'ArrowUp': select(state.selected - 1); break;
      case 'PageDown': select(state.selected + 8); break;
      case 'PageUp': select(state.selected - 8); break;
      case 'Home': if (!ctrl) return; select(0); break;
      case 'End': if (!ctrl) return; select(state.items.length - 1); break;
      case 'Enter':
        if (ctrl) act('reveal');
        else if (!commitInput()) activate();
        break;
      case 'Backspace': {
        if (ctrl || els.query.value || state.editingId || state.committed.length === 0) return;
        e.preventDefault();
        beginEditChip(state.committed[state.committed.length - 1].id);
        return;
      }
      case 'Tab': {
        openAddFilters();
        break;
      }
      case 'Escape':
        // Walk back one step at a time: finish editing, then the typed term, then the tags,
        // and only then slip the palette out of the way.
        if (state.editingId) { finishEdit({ cancel: true }); break; }
        if (els.query.value) {
          els.query.value = '';
          syncQuery(); renderQueryHighlight(); renderHints(); newSearch();
          break;
        }
        if (state.committed.length) { clearQuery(); break; }
        host.post({ type: 'hide' });
        break;
      case 'k': if (!ctrl) return; openActionsMenu(); break;
      case ',': if (!ctrl) return; openSettings(); break;
      case 'c':
        if (!ctrl) return;
        if (e.shiftKey) act('copyName');
        else if (els.query.selectionStart !== els.query.selectionEnd) return; // copying text from the search box
        else act('copyPath');
        break;
      default: return;
    }
    e.preventDefault();
  }, true);

  // ---- host messages ---------------------------------------------------------------

  function onFolderPicked({ purpose, path }) {
    if (purpose === 'addFilterPlace') {
      const prefix = state.folderPickQuery || '';
      state.folderPickQuery = '';
      const token = chipToken(newPlaceChip(path));
      if (!token) return;
      if (state.editingId) finishEdit();
      if (prefix.trim()) commitText(prefix);
      els.query.value = '';
      addCommitted(token);
      syncQuery(); renderQueryHighlight(); renderHints(); newSearch();
      els.query.focus();
      return;
    }
    if (purpose === 'exclude') return addExclusion(path);
    if (purpose === 'root' && !state.settings.roots.some(r => r.toLowerCase() === path.toLowerCase())) {
      state.settings.roots.push(path);
      saveSettings();
      renderSettings();
    }
  }

  /**
   * The palette was opened from a File Explorer window: search that folder. A folder chosen this
   * way is replaced the next time it is opened from somewhere else, but one picked by hand stays.
   */
  function onExplorerFolder(path) {
    if (path) {
      const token = chipToken(newPlaceChip(path, true));
      if (!token) return;
      if (state.editingId) finishEdit();
      const existing = state.committed.find(chip => chip.id === state.explorerChipId);
      if (existing) existing.text = token;
      else {
        const chip = addCommitted(token, { prepend: true });
        state.explorerChipId = chip.id;
      }
      els.query.value = '';
      syncQuery(); renderQueryHighlight(); renderHints(); newSearch();
      if (state.view === 'main') toast(`Searching in ${folderName(path)}`);
    } else if (state.explorerChipId) {
      removeCommittedChip(state.explorerChipId);
    }
  }

  let refreshTimer = 0;
  function needsRefresh() {
    const s = state.status;
    return s.version !== state.searchedVersion ||
      (s.indexing && (s.items !== state.searchedItems || s.scanned !== state.searchedScanned));
  }

  function scheduleRefresh() {
    if (refreshTimer || state.view !== 'main' || state.pending || !needsRefresh()) return;
    // Throttle changing indexes without restarting the timer on every progress notification.
    refreshTimer = setTimeout(() => {
      refreshTimer = 0;
      if (state.view === 'main' && !state.pending && !menu.isOpen && needsRefresh())
        runSearch({ keepSelection: true });
    }, state.status.indexing ? 1200 : 400);
  }

  function onStatus(status) {
    state.status = status;
    renderStatus();
    scheduleRefresh();
  }

  host.listen(message => {
    onPredictionMessage(message);
    switch (message.type) {
      case 'init':
        state.settings = message.settings;
        state.places = message.places || [];
        state.types = message.types || [];
        state.home = (state.places.find(p => p.label === 'Home') || {}).path || '';
        state.version = message.version || '';
        state.sort = state.settings.defaultSort || 'relevance';
        document.documentElement.dataset.theme = message.theme;
        document.documentElement.dataset.backdrop = message.backdrop ? 'acrylic' : 'none';
        applySettings();
        renderFilters();
        runSearch();
        renderStatus();
        els.query.focus();
        if (message.hotkeyOk === false) toast(`${state.settings.hotkey} is in use by another app — pick a shortcut in Settings`, true);
        break;
      case 'settings':
        state.settings = message.settings;
        document.documentElement.dataset.theme = message.theme;
        applySettings();
        if (state.view === 'settings' && !state.recording && document.activeElement?.id !== 'exclude-input') renderSettings();
        break;
      case 'theme': document.documentElement.dataset.theme = message.theme; break;
      case 'status': onStatus(message); break;
      case 'explorerFolder': onExplorerFolder(message.path); break;
      case 'contextOpen':
        if (message.path) {
          const token = chipToken(newPlaceChip(message.path, true));
          const chips = setQuery(token);
          state.explorerChipId = chips[chips.length - 1]?.id || 0;
        } else setQuery('');
        break;
      case 'results':
        try { onResults(message); }
        catch (error) {
          state.pending = false;
          state.items = [];
          state.error = error?.message || String(error);
          renderList();
          renderCount();
        }
        break;
      case 'preview': onPreviewText(message); break;
      case 'videoSource': videoPreview.source(message); break;
      case 'paletteVisibility':
        paletteVisible = message.visible;
        if (!paletteVisible) clearPreview();
        else if (state.view === 'main') renderPreview();
        break;
      case 'folderPicked': onFolderPicked(message); break;
      case 'toast': toast(message.text, message.error); break;
      case 'openSettings': openSettings(); break;
      case 'setQuery': if (state.view === 'settings') closeSettings(); setQuery(message.query); break;
      case 'shown':
        if (state.view === 'main') { els.query.focus(); els.query.select(); }
        runSearch({ keepSelection: true });
        requestAnimationFrame(() => host.post({ type: 'painted' }));
        break;
    }
  });

  // The host sizes the window in physical pixels; tell it how many of those one CSS pixel covers
  // (display scaling and the Windows text-size setting both feed into this).
  let reportedScale = window.devicePixelRatio;
  window.addEventListener('resize', () => {
    if (window.devicePixelRatio === reportedScale) return;
    reportedScale = window.devicePixelRatio;
    host.post({ type: 'scale', value: reportedScale });
  });

  $$('[data-icon]').forEach(el => (el.innerHTML = icon(el.dataset.icon, +el.dataset.size || 16)));
  host.post({ type: 'ready', scale: reportedScale });
})();
