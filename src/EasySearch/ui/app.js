// EasySearch interface. Talks to the Windows host over WebView2 messages; when opened in a
// plain browser it falls back to the mock host in mock.js so the UI can be worked on alone.
(() => {
  'use strict';

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const esc = text => String(text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const demo = new URLSearchParams(location.search).has('demo'); // sample data inside the real window
  const hosted = !!window.chrome?.webview && !demo;
  const host = hosted
    ? {
        post: message => window.chrome.webview.postMessage(message),
        listen: handler => window.chrome.webview.addEventListener('message', e => handler(e.data)),
      }
    : window.createMockHost();
  if (!window.chrome?.webview) document.documentElement.classList.add('mock'); // plain browser: draw a stand-in desktop

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
    { id: 'all', label: 'All' },
    { id: 'folder', label: 'Folders' },
    { id: 'document', label: 'Documents' },
    { id: 'image', label: 'Images' },
    { id: 'video', label: 'Video' },
    { id: 'audio', label: 'Audio' },
    { id: 'code', label: 'Code' },
    { id: 'archive', label: 'Archives' },
    { id: 'app', label: 'Apps' },
  ];

  const KB = 1024, MB = KB * 1024, GB = MB * 1024, DAY = 86400000;
  const SIZES = [
    { id: null, label: 'Any size' },
    { id: 'empty', label: 'Empty', min: 0, max: 0 },
    { id: 'tiny', label: 'Under 100 KB', min: 1, max: 100 * KB - 1 },
    { id: 'small', label: '100 KB – 1 MB', min: 100 * KB, max: MB - 1 },
    { id: 'medium', label: '1 – 100 MB', min: MB, max: 100 * MB - 1 },
    { id: 'large', label: '100 MB – 1 GB', min: 100 * MB, max: GB - 1 },
    { id: 'huge', label: 'Over 1 GB', min: GB },
  ];
  const startOfToday = () => new Date().setHours(0, 0, 0, 0);
  const DATES = [
    { id: null, label: 'Any time' },
    { id: 'today', label: 'Today', after: () => startOfToday() },
    { id: 'week', label: 'Last 7 days', after: () => Date.now() - 7 * DAY },
    { id: 'month', label: 'Last 30 days', after: () => Date.now() - 30 * DAY },
    { id: 'year', label: 'This year', after: () => new Date(new Date().getFullYear(), 0, 1).getTime() },
    { id: 'old', label: 'Over a year ago', before: () => Date.now() - 365 * DAY },
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
    for (const token of query.match(/"[^"]*"|\S+/g) || []) {
      let text = token.replace(/"/g, '');
      if (text.startsWith('!')) continue;
      if (/^(ext|type|kind|size|dm|modified|date|datemodified|in|under|parent|path|regex|folders?|files?):/i.test(text)) continue;
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
    query: '',
    kind: 'all',
    size: null,
    modified: null,
    scope: null, // { label, path }
    sort: 'relevance',
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
    window: $('#window'), query: $('#query'), count: $('#count'), kinds: $('#kinds'), list: $('#list'),
    preview: $('#preview'), status: $('#status'), hints: $('#hints'), settings: $('#settings'),
    menu: $('#menu'), toast: $('#toast'),
  };

  const iconCache = new Map(); // extension -> icon URL, so one request serves every file of a type
  const PER_FILE_ICONS = new Set(['exe', 'lnk', 'ico', 'cur', 'url', 'scr', 'msi']);

  function iconUrl(item, size) {
    if (!hosted) return null;
    const path = pathOf(item);
    const url = `https://images.easysearch.example/icon?s=${size}&p=${encodeURIComponent(path)}`;
    const ext = item.isDir ? '' : extOf(item.name);
    if (!ext || PER_FILE_ICONS.has(ext)) return url;
    const key = `${ext}|${size}`;
    if (!iconCache.has(key)) iconCache.set(key, url);
    return iconCache.get(key);
  }

  const thumbUrl = item =>
    `https://images.easysearch.example/thumb?s=512&p=${encodeURIComponent(pathOf(item))}&m=${item.modified}`;

  // ---- searching -------------------------------------------------------------------

  const hasFilters = () => state.kind !== 'all' || state.size || state.modified || state.scope;
  const isSearching = () => state.query.trim().length > 0 || hasFilters();

  function runSearch({ keepSelection = false } = {}) {
    if (!isSearching()) {
      state.searchId++;
      state.pending = false;
      showHome();
      return;
    }

    const size = SIZES.find(s => s.id === state.size) || {};
    const date = DATES.find(d => d.id === state.modified) || {};
    const sort = SORTS.find(s => s.id === state.sort) || SORTS[0];
    state.pending = true;
    state.keepSelection = keepSelection;
    state.searchedVersion = state.status.version;
    host.post({
      type: 'search',
      id: ++state.searchId,
      query: state.query.trim(),
      kind: state.kind,
      sizeMin: size.min ?? null,
      sizeMax: size.max ?? null,
      modifiedAfter: date.after ? date.after() : null,
      modifiedBefore: date.before ? date.before() : null,
      scope: state.scope ? state.scope.path : null,
      sort: sort.id,
      desc: !!sort.desc,
      limit: state.limit,
      matchCase: state.options.matchCase,
      matchPath: state.options.matchPath,
      regex: state.options.regex,
      showHidden: state.settings.showHidden,
      showSystem: state.settings.showSystem,
    });
  }

  function newSearch() {
    state.limit = 150;
    runSearch();
  }

  function onResults(message) {
    if (message.id !== state.searchId) return;
    const previous = state.keepSelection ? selectedItem() : null;
    state.pending = false;
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

  function homeCommands() {
    const place = label => state.places.find(p => p.label === label);
    const commands = [
      { section: 'Quick filters' },
      { command: true, icon: 'document', kind: 'document', title: 'Recent documents', note: 'Documents, newest first', run: () => applyPreset({ kind: 'document', sort: 'modified' }) },
      { command: true, icon: 'image', kind: 'image', title: 'Images from the last 7 days', note: 'Photos, screenshots and graphics', run: () => applyPreset({ kind: 'image', modified: 'week', sort: 'modified' }) },
      { command: true, icon: 'archive', kind: 'archive', title: 'Large files', note: 'Everything over 1 GB, biggest first', run: () => applyPreset({ size: 'huge', sort: 'size' }) },
      { command: true, icon: 'folder', kind: 'folder', title: 'Recently changed folders', note: 'Where work has been happening', run: () => applyPreset({ kind: 'folder', sort: 'modified' }) },
    ];
    const downloads = place('Downloads');
    if (downloads)
      commands.push({ command: true, icon: 'clock', kind: 'accent', title: 'Latest downloads', note: breadcrumbText(downloads.path), run: () => applyPreset({ scope: downloads, sort: 'modified' }) });

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
    state.kind = preset.kind || 'all';
    state.size = preset.size || null;
    state.modified = preset.modified || null;
    state.scope = preset.scope || null;
    state.sort = preset.sort || state.settings.defaultSort;
    renderFilters();
    newSearch();
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

  function rowIcon(item, size = 18) {
    if (item.command) return `<span class="row-icon k-${item.kind}">${icon(item.icon, size)}</span>`;
    const kind = kindOf(item);
    const url = iconUrl(item, 64);
    const img = url
      ? `<img src="${esc(url)}" alt="" loading="lazy" onload="this.parentNode.classList.add('has-img')" onerror="this.remove()">`
      : '';
    return `<span class="row-icon k-${kind}">${icon(kindIcon(kind), size)}${img}</span>`;
  }

  function renderList({ keepScroll = false } = {}) {
    const scrollTop = els.list.scrollTop;
    let html = '';

    if (!isSearching()) {
      let index = 0;
      for (const entry of state.sections) {
        if (entry.section) { html += `<div class="section">${esc(entry.section)}</div>`; continue; }
        html += `<div class="row cmd${index === state.selected ? ' sel' : ''}" data-i="${index}" role="option">
          ${rowIcon(entry)}
          <span class="row-main"><span class="row-name">${esc(entry.title)}</span><span class="row-path">${esc(entry.note)}</span></span>
        </div>`;
        index++;
      }
    } else if (state.error) {
      html = `<div class="empty error">${icon('info', 34)}<h2>That search isn’t quite right</h2><p>${esc(state.error)}</p></div>`;
    } else if (state.items.length === 0) {
      const hidden = state.settings.showHidden ? '' : ' Hidden items are off — <span class="link" data-act="show-hidden">show them</span>.';
      const indexing = state.status.indexing ? ' The index is still being built, so more may turn up.' : '';
      const reset = hasFilters() ? ' <span class="link" data-act="reset">Clear filters</span>.' : '';
      html = `<div class="empty">${icon('search', 34)}<h2>No results</h2><p>Nothing matches that search.${reset}${hidden}${indexing}</p></div>`;
    } else {
      const terms = highlightTerms(state.query);
      state.items.forEach((item, index) => {
        html += `<div class="row${index === state.selected ? ' sel' : ''}${item.hidden ? ' dim' : ''}" data-i="${index}" role="option">
          ${rowIcon(item)}
          <span class="row-main"><span class="row-name">${highlight(item.name, terms)}</span><span class="row-path">${breadcrumb(item.dir)}</span></span>
          <span class="row-meta"><b>${formatDate(item.modified)}</b><span>${item.isDir ? 'Folder' : formatSize(item.size)}</span></span>
        </div>`;
      });
    }

    els.list.innerHTML = html;
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
    if (!isSearching() || state.error) { els.count.textContent = ''; return; }
    const total = number.format(state.total);
    const time = state.elapsed < 10 ? state.elapsed.toFixed(1) : Math.round(state.elapsed);
    els.count.textContent = `${total} ${state.total === 1 ? 'result' : 'results'} · ${time} ms`;
  }

  function maybeLoadMore() {
    if (state.pending || !isSearching() || state.items.length >= state.total || state.limit >= 5000) return;
    if (els.list.scrollTop + els.list.clientHeight < els.list.scrollHeight - 400) return;
    state.limit = Math.min(5000, state.limit + 350);
    runSearch({ keepSelection: true });
  }

  // ---- preview ---------------------------------------------------------------------

  const TIPS = [
    ['report 2026', 'Every word, in any order'],
    ['jpg|png', 'Either one'],
    ['!draft', 'Leave matches out'],
    ['"annual report"', 'An exact phrase'],
    ['*.log', 'Wildcards'],
    ['ext:pdf', 'By extension'],
    ['size:>100mb', 'By size, or size:1mb..1gb'],
    ['dm:7d', 'Changed lately — also today, 2026-03'],
    ['in:C:\\Projects', 'Only inside a folder'],
    ['src\\ main', 'A backslash matches the path'],
    ['regex:^IMG_\\d+', 'Regular expressions'],
  ];

  let previewTimer = 0;

  function renderPreview() {
    if (!state.settings?.showPreview) return;
    const item = selectedItem();
    if (!item || item.command) {
      els.preview.innerHTML = `
        <div class="tips-title">Search like a pro</div>
        <div class="tips-sub">Click an example to try it.</div>
        <div class="tips">${TIPS.map(([code, text]) => `<code data-tip="${esc(code)}">${esc(code)}</code><span>${esc(text)}</span>`).join('')}</div>`;
      return;
    }

    const path = pathOf(item);
    const kind = kindOf(item);
    const visual = kind === 'image' || kind === 'video' || extOf(item.name) === 'pdf';
    const hero = hosted
      ? `<img class="${visual ? '' : 'icon'}" src="${esc(visual ? thumbUrl(item) : iconUrl(item, 256))}" alt="" onerror="this.replaceWith(this.nextElementSibling.content.cloneNode(true))"><template>${rowIcon({ ...item, command: false }, 34).replace(/<img[^>]*>/, '')}</template>`
      : `<span class="row-icon k-${kind}">${icon(kindIcon(kind), 34)}</span>`;

    els.preview.innerHTML = `
      <div class="pv-hero">${hero}</div>
      <div class="pv-name">${esc(item.name)}</div>
      <div class="pv-kind">${esc(typeName(item))}${item.isDir ? '' : ' · ' + formatSize(item.size)}</div>
      <dl class="pv-meta">
        <dt>Where</dt><dd class="path">${esc(item.dir || path)}</dd>
        <dt>Modified</dt><dd>${esc(longDate.format(new Date(item.modified)))}</dd>
        ${item.isDir ? '' : `<dt>Size</dt><dd>${formatSize(item.size)}${item.size >= KB ? ` (${number.format(item.size)} bytes)` : ''}</dd>`}
        ${item.hidden ? '<dt>Attributes</dt><dd>Hidden</dd>' : ''}
      </dl>
      <pre class="pv-text" id="pv-text" hidden></pre>`;

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
      ? `Indexing… ${number.format(Math.max(s.scanned, s.items))} items`
      : `${number.format(s.items)} items indexed`;
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
    if (!item) els.hints.innerHTML = hint('Filters', 'Tab') + hint('Close', 'Esc');
    else if (item.command) els.hints.innerHTML = hint('Run', '↵') + hint('Filters', 'Tab') + hint('Settings', 'Ctrl', ',');
    else els.hints.innerHTML = hint('Open', '↵') + hint('Reveal', 'Ctrl', '↵') + hint('Actions', 'Ctrl', 'K');
  }

  // ---- filters ---------------------------------------------------------------------

  function renderFilters() {
    els.kinds.innerHTML = KINDS.map(k =>
      `<button class="kind${k.id === state.kind ? ' on' : ''}" data-kind="${k.id}" role="tab">${k.label}</button>`).join('');

    const chip = (id, label, active) => {
      const el = $(id);
      el.classList.toggle('on', !!active);
      el.innerHTML = `<span>${esc(label)}</span>${icon('chevron', 13)}`;
    };
    chip('#chip-size', state.size ? SIZES.find(s => s.id === state.size).label : 'Size', state.size);
    chip('#chip-modified', state.modified ? DATES.find(d => d.id === state.modified).label : 'Modified', state.modified);
    chip('#chip-scope', state.scope ? state.scope.label : 'Everywhere', state.scope);

    const o = state.options;
    $('#btn-options').classList.toggle('active', o.matchCase || o.matchPath || o.regex || state.sort !== 'relevance');
  }

  function setKind(kind) {
    state.kind = kind;
    renderFilters();
    newSearch();
  }

  function resetFilters() {
    state.kind = 'all';
    state.size = state.modified = state.scope = null;
    renderFilters();
    newSearch();
  }

  function setQuery(text) {
    els.query.value = text;
    state.query = text;
    els.query.focus();
    newSearch();
  }

  const folderName = path => path.replace(/\\+$/, '').split('\\').pop() || path;

  function openSizeMenu() {
    menu.open($('#chip-size'), SIZES.map(s => ({
      label: s.label, checked: s.id === state.size, run: () => { state.size = s.id; renderFilters(); newSearch(); },
    })));
  }

  function openModifiedMenu() {
    menu.open($('#chip-modified'), DATES.map(d => ({
      label: d.label, checked: d.id === state.modified, run: () => { state.modified = d.id; renderFilters(); newSearch(); },
    })));
  }

  function openScopeMenu() {
    const choose = scope => { state.scope = scope; renderFilters(); newSearch(); };
    const same = path => state.scope && state.scope.path.toLowerCase() === path.toLowerCase();
    const items = [{ label: 'Everywhere', checked: !state.scope, run: () => choose(null) }];
    if (state.places.length) items.push({ head: 'Places' });
    for (const place of state.places)
      items.push({ label: place.label, checked: same(place.path), run: () => choose(place) });
    items.push({ head: 'Indexed locations' });
    for (const root of state.settings.roots)
      items.push({ label: root, checked: same(root), run: () => choose({ label: folderName(root), path: root }) });
    if (state.scope && !items.some(i => i.checked))
      items.push({ sep: true }, { label: state.scope.label, sub: 'Current', checked: true, run: () => {} });
    items.push({ sep: true }, { label: 'Choose folder…', run: () => host.post({ type: 'pickFolder', purpose: 'scope' }) });
    menu.open($('#chip-scope'), items, { align: 'right' });
  }

  function openOptionsMenu() {
    const toggle = key => () => { state.options[key] = !state.options[key]; renderFilters(); newSearch(); };
    const setting = key => () => { state.settings[key] = !state.settings[key]; saveSettings(); newSearch(); };
    menu.open($('#btn-options'), [
      { head: 'Sort by' },
      ...SORTS.map(s => ({ label: s.label, checked: s.id === state.sort, run: () => { state.sort = s.id; renderFilters(); newSearch(); } })),
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
      state.scope = { label: folderName(path), path };
      els.query.value = state.query = '';
      renderFilters();
      newSearch();
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

    open(anchor, items, { align = 'left', corner = false } = {}) {
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
        return `<button class="menu-item" data-m="${n++}">${lead}<span class="label">${esc(item.label)}</span>${tail}</button>`;
      }).join('');
      this.actionable = actionable;
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
  }

  function openSettings(tab) {
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
                .replace('<div class="field-text">', `<span class="row-icon k-folder">${icon(/^[a-z]:\\?$/i.test(root) ? 'drive' : 'folder', 15)}</span><div class="field-text">`)).join('')}
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

  els.query.addEventListener('input', () => {
    state.query = els.query.value;
    newSearch();
  });

  els.kinds.addEventListener('click', e => {
    const kind = e.target.closest('[data-kind]');
    if (kind) setKind(kind.dataset.kind);
  });

  $('#chip-size').addEventListener('click', openSizeMenu);
  $('#chip-modified').addEventListener('click', openModifiedMenu);
  $('#chip-scope').addEventListener('click', openScopeMenu);
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
    if (state.view === 'main' && e.target !== els.query && !e.target.closest('.preview')) e.preventDefault();
  }, true);
  document.addEventListener('click', e => {
    if (menu.swallow) { menu.swallow = false; e.preventDefault(); e.stopPropagation(); }
  }, true);
  document.addEventListener('contextmenu', e => e.preventDefault());

  document.addEventListener('keydown', e => {
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
      case 'Enter': if (ctrl) act('reveal'); else activate(); break;
      case 'Tab': {
        const at = KINDS.findIndex(k => k.id === state.kind);
        setKind(KINDS[(at + (e.shiftKey ? -1 : 1) + KINDS.length) % KINDS.length].id);
        break;
      }
      case 'Escape':
        if (state.query) setQuery('');
        else if (hasFilters()) resetFilters();
        else host.post({ type: 'hide' });
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
    if (purpose === 'scope') {
      state.scope = { label: folderName(path), path };
      renderFilters();
      return newSearch();
    }
    if (purpose === 'exclude') return addExclusion(path);
    if (purpose === 'root' && !state.settings.roots.some(r => r.toLowerCase() === path.toLowerCase())) {
      state.settings.roots.push(path);
      saveSettings();
      renderSettings();
    }
  }

  let refreshTimer = 0;
  function onStatus(status) {
    state.status = status;
    renderStatus();
    // Results on screen go stale when the index changes underneath them: refresh quietly.
    if (state.view === 'main' && isSearching() && !state.pending && status.version !== state.searchedVersion ||
        (status.indexing && isSearching() && !state.pending && state.items.length < 150)) {
      clearTimeout(refreshTimer);
      refreshTimer = setTimeout(() => { if (!menu.isOpen) runSearch({ keepSelection: true }); }, status.indexing ? 1200 : 400);
    }
  }

  host.listen(message => {
    switch (message.type) {
      case 'init':
        state.settings = message.settings;
        state.places = message.places || [];
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
      case 'results': onResults(message); break;
      case 'preview': onPreviewText(message); break;
      case 'folderPicked': onFolderPicked(message); break;
      case 'toast': toast(message.text, message.error); break;
      case 'openSettings': openSettings(); break;
      case 'setQuery': if (state.view === 'settings') closeSettings(); setQuery(message.query); break;
      case 'shown':
        els.window.classList.remove('appear');
        void els.window.offsetWidth;
        els.window.classList.add('appear');
        if (state.view === 'main') { els.query.focus(); els.query.select(); }
        if (isSearching()) runSearch({ keepSelection: true });
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
