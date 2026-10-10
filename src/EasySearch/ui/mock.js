// Stand-in for the Windows host, used only when the interface is opened in a normal browser
// (for example `python -m http.server` in this folder). It serves a made-up file tree so the
// design can be worked on without building the app. The real host ignores this file's output.
window.createMockHost = () => {
  'use strict';

  const HOME = 'C:\\Users\\alex';
  const DAY = 86400000;
  let seed = 7;
  const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const pick = list => list[Math.floor(random() * list.length)];

  const files = [];
  const add = (dir, name, { isDir = false, size = 0, age = 0, hidden = false } = {}) =>
    files.push({ name, dir, isDir, size: isDir ? 0 : Math.round(size), modified: Date.now() - age * DAY, hidden });

  const folders = [
    'Desktop', 'Documents', 'Documents\\Invoices', 'Documents\\Taxes 2025', 'Documents\\Reports', 'Downloads',
    'Pictures', 'Pictures\\Lisbon 2026', 'Pictures\\Screenshots', 'Music', 'Videos', 'Projects',
    'Projects\\easysearch', 'Projects\\easysearch\\src', 'Projects\\portfolio-site', 'Projects\\portfolio-site\\assets',
    'Projects\\budget-app', 'Projects\\budget-app\\src', 'Projects\\notes',
  ];
  add('C:\\Users', 'alex', { isDir: true, age: 0.1 });
  for (const folder of folders) {
    const cut = folder.lastIndexOf('\\');
    add(cut < 0 ? HOME : `${HOME}\\${folder.slice(0, cut)}`, folder.slice(cut + 1), { isDir: true, age: random() * 40 });
  }

  const named = [
    ['Documents\\Reports', 'Quarterly report Q1 2026.pdf', 2.4e6, 2], ['Documents\\Reports', 'Quarterly report Q4 2025.pdf', 2.1e6, 96],
    ['Documents\\Reports', 'Annual report draft.docx', 480e3, 0.2], ['Documents\\Reports', 'Report figures.xlsx', 96e3, 3],
    ['Documents', 'Lease agreement.pdf', 310e3, 210], ['Documents', 'Resume 2026.docx', 64e3, 12], ['Documents', 'Reading list.md', 3e3, 1],
    ['Documents\\Invoices', 'Invoice 1042 - Northwind.pdf', 88e3, 5], ['Documents\\Invoices', 'Invoice 1043 - Contoso.pdf', 91e3, 4],
    ['Documents\\Taxes 2025', 'Tax return 2025.pdf', 1.2e6, 150], ['Documents\\Taxes 2025', 'Receipts.zip', 48e6, 151],
    ['Downloads', 'ubuntu-26.04-desktop-amd64.iso', 5.9e9, 9], ['Downloads', 'node-v24.15.0-x64.msi', 31e6, 20],
    ['Downloads', 'Figma-Setup.exe', 142e6, 33], ['Downloads', 'conference-talk.mp4', 1.4e9, 6], ['Downloads', 'dataset.csv', 220e6, 2],
    ['Desktop', 'Meeting notes.txt', 2e3, 0.05], ['Desktop', 'Budget 2026.xlsx', 54e3, 1.5], ['Desktop', 'EasySearch.lnk', 1.4e3, 30],
    ['Projects\\easysearch', 'README.md', 5.7e3, 0.3], ['Projects\\easysearch', 'EasySearch.slnx', 420, 0.4],
    ['Projects\\easysearch\\src', 'SearchEngine.cs', 12.4e3, 0.1], ['Projects\\easysearch\\src', 'FileIndex.cs', 15.1e3, 0.2],
    ['Projects\\easysearch\\src', 'app.js', 31e3, 0.02], ['Projects\\easysearch\\src', 'styles.css', 14e3, 0.03],
    ['Projects\\portfolio-site', 'index.html', 8e3, 14], ['Projects\\portfolio-site', 'package.json', 900, 14],
    ['Projects\\portfolio-site\\assets', 'hero.webp', 380e3, 15], ['Projects\\portfolio-site\\assets', 'logo.svg', 4e3, 40],
    ['Projects\\budget-app\\src', 'main.py', 6e3, 60], ['Projects\\budget-app\\src', 'report_builder.py', 9e3, 58],
    ['Projects\\notes', 'ideas.md', 2e3, 0.6], ['Music', 'Morning mix.flac', 38e6, 300], ['Music', 'Podcast intro.mp3', 4.1e6, 45],
    ['Videos', 'Screen recording 2026-10-02.mp4', 86e6, 8], ['Videos', 'Lisbon drone.mov', 2.2e9, 120],
  ];
  for (const [folder, name, size, age] of named) add(`${HOME}\\${folder}`, name, { size, age });

  for (let i = 0; i < 46; i++)
    add(`${HOME}\\Pictures\\Lisbon 2026`, `IMG_${4301 + i}.${i % 9 === 0 ? 'HEIC' : 'JPG'}`, { size: 1.5e6 + random() * 3e6, age: 118 + random() * 6 });
  for (let i = 0; i < 24; i++)
    add(`${HOME}\\Pictures\\Screenshots`, `Screenshot 2026-${String(1 + (i % 10)).padStart(2, '0')}-${String(3 + i).padStart(2, '0')}.png`, { size: 200e3 + random() * 900e3, age: i * 3 + random() * 2 });
  const words = ['report', 'notes', 'draft', 'summary', 'plan', 'budget', 'design', 'backup', 'export', 'final', 'review', 'log'];
  const exts = ['pdf', 'docx', 'txt', 'md', 'png', 'json', 'zip', 'xlsx', 'js', 'py', 'log', 'mp3'];
  for (let i = 0; i < 700; i++)
    add(`${HOME}\\${pick(folders)}`, `${pick(words)}-${pick(words)}-${100 + i}.${pick(exts)}`, { size: Math.pow(10, 2 + random() * 6), age: random() * 900 });
  add(HOME, 'AppData', { isDir: true, age: 1, hidden: true });
  add(`${HOME}\\AppData`, 'report-cache.json', { size: 12e3, age: 1, hidden: true });

  const KINDS = {
    image: 'jpg jpeg png gif bmp webp svg heic', video: 'mp4 mkv mov avi webm', audio: 'mp3 wav flac aac ogg m4a',
    document: 'pdf doc docx xls xlsx ppt pptx txt md csv', code: 'js ts py cs html css json xml yaml slnx',
    archive: 'zip rar 7z tar gz iso', app: 'exe msi lnk',
  };
  const ext = name => (name.includes('.') ? name.slice(name.lastIndexOf('.') + 1).toLowerCase() : '');

  let settings = {
    theme: 'system', accent: '#0a84ff', hotkey: 'Alt+Space', hideOnBlur: true, launchAtLogin: false, showPreview: true,
    roots: ['C:\\', 'D:\\'], excluded: ['$Recycle.Bin', 'System Volume Information', 'node_modules', '.git', 'C:\\Windows\\WinSxS'],
    showHidden: false, showSystem: false, defaultSort: 'relevance',
  };
  const status = { type: 'status', indexing: false, items: 1474675, scanned: 0, version: 1, lastIndexed: Date.now() - 4 * 60000, durationMs: 9280 };
  const systemTheme = () => (matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark');
  const params = new URLSearchParams(location.search);
  const theme = () => params.get('theme') || (settings.theme === 'system' ? systemTheme() : settings.theme);

  let handler = () => {};
  const send = message => setTimeout(() => handler(message), 0);

  function search(request) {
    const started = performance.now();
    const terms = [];
    let extFilter = null, error = null;
    // The mock only understands the symbols, so spell the capitalised logic words that way.
    const spelled = request.query.replace(/\s+OR\s+/g, '|').replace(/\bAND\s+/g, '').replace(/\bNOT\s+/g, '!');
    for (const token of spelled.toLowerCase().match(/"[^"]*"|\S+/g) || []) {
      const text = token.replace(/"/g, '');
      if (text.startsWith('ext:')) extFilter = text.slice(4).split(/[;,|]/);
      else if (text.startsWith('size:') && !/^size:[<>=]*[\d.]+(b|kb|mb|gb)?/.test(text)) error = `Can't read size "${text.slice(5)}". Try size:>10mb or size:1mb..1gb`;
      else if (!/^\w+:/.test(text) || /^[a-z]:\\/.test(text)) terms.push(text);
    }
    if (error) return { error, items: [], total: 0, elapsedMs: 0 };

    const kindExts = KINDS[request.kind] ? KINDS[request.kind].split(' ') : null;
    const customExts = (request.extensions || '').toLowerCase().split(/[\s,;|]+/).map(e => e.replace(/^[*.]+/, '')).filter(Boolean);
    const scope = request.scope ? request.scope.toLowerCase().replace(/\\$/, '') : null;
    const matches = files.filter(file => {
      if (file.hidden && !request.showHidden) return false;
      if (request.kind === 'folder' && !file.isDir) return false;
      if (kindExts && (file.isDir || !kindExts.includes(ext(file.name)))) return false;
      if (extFilter && (file.isDir || !extFilter.includes(ext(file.name)))) return false;
      if (customExts.length && (file.isDir || !customExts.includes(ext(file.name)))) return false;
      if (request.sizeMin != null && (file.isDir || file.size < request.sizeMin)) return false;
      if (request.sizeMax != null && (file.isDir || file.size > request.sizeMax)) return false;
      if (request.modifiedAfter != null && file.modified < request.modifiedAfter) return false;
      if (request.modifiedBefore != null && file.modified >= request.modifiedBefore) return false;
      if (scope && !`${file.dir}\\`.toLowerCase().startsWith(`${scope}\\`)) return false;
      const name = file.name.toLowerCase(), path = `${file.dir}\\${file.name}`.toLowerCase();
      return terms.every(term => term.split('|').some(t => (t.includes('\\') || request.matchPath ? path : name).includes(t)));
    });

    const first = terms[0] || '';
    const score = file => {
      const at = first ? file.name.toLowerCase().indexOf(first) : -1;
      return (at === 0 ? 400 : at > 0 ? 100 : 0) - file.name.length + (file.isDir ? 40 : 0) - Math.min(60, (Date.now() - file.modified) / DAY / 10);
    };
    const direction = request.desc ? -1 : 1;
    const sort = request.sort === 'relevance' && !first ? 'modified' : request.sort;
    matches.sort({
      relevance: (a, b) => score(b) - score(a),
      name: (a, b) => direction * a.name.localeCompare(b.name),
      path: (a, b) => direction * `${a.dir}\\${a.name}`.localeCompare(`${b.dir}\\${b.name}`),
      size: (a, b) => direction * (a.size - b.size),
      modified: (a, b) => (request.sort === 'relevance' ? -1 : direction) * (a.modified - b.modified),
    }[sort]);

    return { total: matches.length, items: matches.slice(0, request.limit), elapsedMs: performance.now() - started + 1.4 };
  }

  return {
    listen: callback => (handler = callback),
    post(message) {
      switch (message.type) {
        case 'ready':
          send({
            type: 'init', settings, theme: theme(), backdrop: true, version: '0.1.0', hotkeyOk: true,
            places: ['Home', 'Desktop', 'Documents', 'Downloads', 'Pictures', 'Music', 'Videos']
              .map(label => ({ label, path: label === 'Home' ? HOME : `${HOME}\\${label}` })),
          });
          send(status);
          if (params.get('q')) send({ type: 'setQuery', query: params.get('q') });
          break;
        case 'search': send({ type: 'results', id: message.id, ...search(message) }); break;
        case 'preview':
          if (/\.(md|txt|js|py|cs|css|json|html)$/i.test(message.path))
            send({ type: 'preview', id: message.id, path: message.path, text: '# Notes\n\nThis is preview text from the mock host.\nThe real app shows the first lines of the file here.\n\n- instant search\n- filters for type, size and date\n- live index updates\n' });
          break;
        case 'action':
          send({ type: 'toast', text: { copyPath: 'Path copied', copyName: 'Name copied', copyFile: 'Copied — paste it anywhere' }[message.action] || `${message.action}: ${message.path}` });
          break;
        case 'setSettings':
          settings = message.settings;
          send({ type: 'settings', settings, theme: theme(), hotkeyOk: true });
          break;
        case 'pickFolder': send({ type: 'folderPicked', purpose: message.purpose, path: 'D:\\Archive\\Old projects' }); break;
        case 'reindex':
          status.indexing = true; status.scanned = 0;
          const timer = setInterval(() => {
            status.scanned += 184000;
            if (status.scanned >= status.items) { status.indexing = false; status.lastIndexed = Date.now(); status.version++; clearInterval(timer); }
            send({ ...status });
          }, 250);
          break;
      }
    },
  };
};
