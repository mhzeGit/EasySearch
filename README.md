# EasySearch

Instant file and folder search for Windows, in a frosted-glass command palette.
Press **Alt+Space** anywhere, type a few letters, hit Enter.

![EasySearch](docs/screenshot.png)

> Prototype. It works end to end and is fast, but see [Status](#status) for what is still rough.

## What it does

- **Searches everything as you type.** Every file and folder in the locations you choose is held in
  an in-memory index, so results update on each keystroke (a few milliseconds over 1.5 million items).
- **Stays current.** The index follows file system changes live, is cached on disk for an instant
  start, and is refreshed in the background each time the app launches.
- **Filters.** Type (folders, documents, images, video, audio, code, archives, apps), size, date
  modified, and "only inside this folder" — as one-click chips or typed operators.
- **Scope control.** Pick which drives and folders are indexed and which folders are left out
  (by name anywhere, like `node_modules`, or by full path). Hidden and system items are opt-in.
- **Palette workflow.** Global hotkey, keyboard-first, real Explorer icons, a preview pane with
  thumbnails and text snippets, and an actions menu (open, reveal, copy path, open in Terminal…).
- **Looks the part.** A blurred, see-through window in light or dark, with a choice of accent colour.

## Run it

Requires Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the WebView2
runtime (already part of Windows 11).

```bash
dotnet run --project src/EasySearch -c Release
```

The first launch indexes every fixed drive; results appear while it works. After that, EasySearch
lives in the notification area — summon it with the hotkey or by clicking its icon.

To build a folder you can copy elsewhere:

```bash
dotnet publish src/EasySearch -c Release -r win-x64 --self-contained false -o publish
```

## Searching

| Type this | To find |
| --- | --- |
| `report 2026` | names containing every word, in any order |
| `jpg\|png` | either one |
| `!draft` | everything except matches |
| `"annual report"` | an exact phrase |
| `*.log`, `img_????.jpg` | wildcards (match the whole name) |
| `ext:pdf`, `ext:jpg;png` | by extension |
| `type:image` | by kind: `folder`, `file`, `image`, `video`, `audio`, `document`, `code`, `archive`, `app` |
| `size:>100mb`, `size:1mb..1gb`, `size:empty` | by size (`tiny` … `gigantic` also work) |
| `dm:today`, `dm:7d`, `dm:2026-03`, `dm:>2026-01-01` | by date modified |
| `in:C:\Projects` | only inside a folder |
| `src\ main`, `path:backup` | a backslash (or `path:`) matches against the full path |
| `regex:^IMG_\d+` | regular expressions |

Operators combine freely: `type:image dm:30d size:>2mb !screenshot`.

### Keys

| Key | Action |
| --- | --- |
| `Alt+Space` | show / hide (change it in Settings) |
| `↑` `↓` `PgUp` `PgDn` | move through results |
| `Enter` | open |
| `Ctrl+Enter` | reveal in Explorer |
| `Ctrl+K` or right-click | actions menu |
| `Ctrl+C` / `Ctrl+Shift+C` | copy path / copy name |
| `Tab` / `Shift+Tab` | cycle the type filter |
| `Ctrl+,` | settings |
| `Esc` | clear the search, then the filters, then hide |

## How it works

```
src/
  EasySearch.Core/   index, scanner, query parser, search engine, live updates   (no UI, no Windows-only APIs)
  EasySearch/        the Windows app: window, hotkey, tray, icons, and the bridge to the interface
    ui/              the interface itself: plain HTML, CSS and JavaScript shown in WebView2
  EasySearch.Cli/    `esearch`: index a folder and query it from a terminal (also the benchmark)
tests/               xUnit tests that run the engine against a real folder tree
```

- **Index** (`FileIndex`): parallel arrays — name, parent, size, date, flags — with all names back to
  back in one buffer. Paths are never stored; each entry points at its parent folder.
- **Scan** (`IndexBuilder`): folders are walked in parallel; one directory query per folder returns
  names, sizes, dates and attributes together, so there are no per-file system calls.
- **Search** (`SearchEngine`): a query usually contains some literal text, so one vectorised search
  runs across the whole name buffer and only the entries it lands in are evaluated against the rest
  of the query. Each thread keeps just the best hits it needs.
- **Live updates** (`IndexService`): file system notifications are batched and reconciled against
  what is on disk, so creates, renames, moves and deletes all reduce to the same two operations.
- **Window** (`MainForm`, `Backdrop`): a frameless, always-on-top window. The frosted background is
  the app's own: it captures the screen behind the palette as it opens, blurs and tones it, and
  paints it under the transparent interface. (Windows' built-in acrylic turns into a flat colour
  when "Transparency effects" is off, so it is not used.)

Measured on the development machine (12 logical cores, NVMe, 1.47 million items):

| | |
| --- | --- |
| Full scan | 9 s with a warm file cache, about 35 s cold |
| Memory | about 125 MB |
| Typical query (`readme !node`, `*.csproj`) | 4 ms |
| Filter-only query (`type:image dm:30d`) | 30 ms |
| Single letter (937,000 matches) | 100 ms |

## Development

```bash
dotnet test                                   # engine tests
dotnet run --project src/EasySearch.Cli -c Release -- C:\Projects -q "*.csproj" -q "size:>1gb"
python -m http.server 5173 --directory src/EasySearch/ui   # the interface alone, on sample data
```

Useful switches for the app: `--stay` (don't hide when focus is lost), `--query <text>`,
`--settings`, `--demo` (sample data in the real window, for screenshots), `--background`
(start hidden; used by "Launch at login"). Set `EASYSEARCH_DATA` to a folder to keep settings and
the index there instead of in `%APPDATA%` / `%LOCALAPPDATA%`.

`pwsh tools/make-icon.ps1` regenerates the app icon.

## Status

Working and verified: indexing, search and every operator above, live updates, the cache, the
palette window, hotkey, tray, single instance, real icons, preview, settings.

Known gaps:

- **First scan is slower than Everything.** Everything reads the NTFS master file table directly
  (seconds for millions of files, but it needs administrator rights or a service). EasySearch walks
  folders with normal permissions, so a cold first scan takes tens of seconds; after that the cache
  and live updates hide the difference. Reading the MFT / USN journal is the obvious next step.
- **Folders you cannot read are skipped**, for the same reason.
- **The blur is a snapshot**, taken each time the palette opens. A video playing behind it will not
  keep moving through the glass.
- Queries that only match on the path (`path:foo` with nothing else) and regular expressions scan
  every entry and take longer (roughly 70–200 ms at 1.5 million items).
- Folder sizes are not calculated; size filters apply to files.
- No installer yet, and the window cannot be resized.
