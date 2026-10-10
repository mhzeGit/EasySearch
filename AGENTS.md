# Agent instructions

EasySearch is a .NET 10 / WinForms + WebView2 file-search palette for Windows. Source layout and usage are in `README.md`.

- Before finishing implementation work, run `dotnet test` from the repository root and make sure it passes.
- Do not edit, revert or commit changes you did not make. The working tree often holds the user's in-progress edits.

## Build and run after every change

Every change that affects the app (anything under `src\`, `Directory.Build.props`, UI assets) ends with the new build running on this machine, so the user can press the hotkey and try it straight away. Do this after the tests pass and before you report back:

1. Stop the running copy. It locks its own files, so the build fails while it is open, and a second launch only signals the first one and exits:
   `Stop-Process -Name EasySearch -ErrorAction SilentlyContinue`
2. Build: `dotnet build src\EasySearch -c Release`. Fix any error or warning before going on.
3. Start the new build in the background: `Start-Process src\EasySearch\bin\Release\net10.0-windows\EasySearch.exe -ArgumentList '--background'`
   - `--background` starts it in the notification area without opening the window or taking keyboard focus; the user opens it with the hotkey (Win+Space by default). Do not launch it without that switch unless the user asks to see the window: it would interrupt whatever they are typing.
   - Start it with `Start-Process` (detached), not `dotnet run`, which would block and tie the app to your shell.
   - Do not set `EASYSEARCH_DATA` for this launch. It must use the user's real settings and index. That variable is only for your own test runs.
4. Confirm it is up: `Get-Process EasySearch` shows one process a few seconds later. If it exited, find out why before reporting.

Leave it running when you finish. In your final message, say that the new build is running, or why it is not.

Changes that do not alter the app (README, tests only, this file) do not need this.

## Keep the GitHub build current

Source lives in git; builds live only on the GitHub Release `v<version>` as a single zip that is replaced on every publish. `release\` is git-ignored: never commit build output (zips, DLLs, EXEs) to the repository.

Every change that affects the shipped app (anything under `src\`, `Directory.Build.props`, build scripts, UI assets) is not finished until the GitHub Release holds a build of it:

1. Run `dotnet test` and fix failures first.
2. Run `.\publish-release.bat nopause` from the repository root (cmd does not search the current directory, so keep the `.\`). It runs the tests, builds the self-contained `win-x64` app into `release\EasySearch-<version>-win-x64.zip`, and uploads it to the GitHub Release, replacing the previous file. It aborts without publishing if the tests or the build fail. Do not pass `notest` unless the user asks.
3. Commit your source changes and push to `origin`.
   - Stage explicit paths only (never `git add -A` / `git add .`) and commit with `git commit -m "<message>" -- <paths>`, so only those paths are committed even when the index holds other staged changes.
   - The release zip is built from the working tree. If there are unrelated uncommitted changes, say so in your summary.
4. `publish-release.bat` handles the one-time setup itself: the GitHub sign-in and creating the repository (the user chooses public or private). If `git remote` is empty or GitHub sign-in is missing, do not create the repository yourself, do not try to log in, and do not look for tokens. Tell the user to double-click `publish-release.bat` once; the build is not on GitHub until then.
5. Changes that do not alter the app (README, tests only, this file) do not need steps 1-2, but still commit and push them once a remote exists.
6. The version lives in `<Version>` in `Directory.Build.props`. Do not bump it unless the user asks. When they do, bump it there; `publish-release.bat` then creates a new GitHub Release and tag for it instead of overwriting the old one.

In your final message, state the commit hash that was pushed (or that there is no remote yet) and whether the GitHub Release was updated.
