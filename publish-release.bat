@echo off
rem ---------------------------------------------------------------------------
rem  Publish the current build to GitHub: tests -> release zip -> GitHub Release.
rem    publish-release.bat            run the tests, build the zip, upload it to the GitHub Release
rem    publish-release.bat notest     same, without running the tests
rem    publish-release.bat nopause    don't wait for a key at the end (for scripts / agents)
rem  The zip replaces the file already attached to release v<VERSION> (VERSION is read from Directory.Build.props),
rem  so GitHub always holds exactly one, current build and the git history never grows.
rem  First run: signs you in to GitHub in the browser, then creates the GitHub repository (you pick public or
rem  private) and pushes the committed source. That only happens once.
rem ---------------------------------------------------------------------------
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "APP=EasySearch"
set "RID=win-x64"
set "RUN_TESTS=1"
set "PAUSE_AT_END=1"
for %%A in (%*) do (
    if /I "%%A"=="notest" set "RUN_TESTS=0"
    if /I "%%A"=="nopause" set "PAUSE_AT_END=0"
)

set "EXITCODE=0"
call :main
set "EXITCODE=%errorlevel%"
if "%PAUSE_AT_END%"=="1" (
    echo.
    pause
)
exit /b %EXITCODE%

:main
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [error] The .NET SDK was not found. Install the .NET 10 SDK from https://dot.net and try again.
    exit /b 1
)
where git >nul 2>&1
if errorlevel 1 (
    echo [error] Git was not found. Install it from https://git-scm.com and try again.
    exit /b 1
)

rem --- GitHub CLI -------------------------------------------------------------
set "GH="
for /f "delims=" %%G in ('where gh 2^>nul') do if not defined GH set "GH=%%G"
if not defined GH if exist "%ProgramFiles%\GitHub CLI\gh.exe" set "GH=%ProgramFiles%\GitHub CLI\gh.exe"
if not defined GH (
    echo [error] The GitHub CLI was not found. Install it with:  winget install --id GitHub.cli
    exit /b 1
)

call "%GH%" auth status >nul 2>&1
if errorlevel 1 (
    echo.
    echo === Signing in to GitHub - a browser window opens, this is only needed once ===
    call "%GH%" auth login --hostname github.com --git-protocol https --web
    if errorlevel 1 (
        echo [error] Could not sign in to GitHub.
        exit /b 1
    )
)

rem --- version, read from the one place it is defined ---------------------------
set "VERSION="
for /f "delims=" %%V in ('powershell -NoProfile -Command "([xml](Get-Content -Raw Directory.Build.props)).SelectSingleNode('//Version').InnerText.Trim()"') do if not defined VERSION set "VERSION=%%V"
if not defined VERSION (
    echo [error] Could not read ^<Version^> from Directory.Build.props.
    exit /b 1
)
set "OUT=%~dp0release\%APP%-%VERSION%-%RID%"
set "ZIP=%~dp0release\%APP%-%VERSION%-%RID%.zip"

rem --- tests ------------------------------------------------------------------
if "%RUN_TESTS%"=="1" (
    echo.
    echo === 1/4  Running tests ===
    dotnet test tests\EasySearch.Tests --nologo -v q
    if errorlevel 1 (
        echo [error] Tests failed - nothing was published.
        exit /b 1
    )
) else (
    echo.
    echo === 1/4  Tests skipped ===
)

rem --- build ------------------------------------------------------------------
echo.
echo === 2/4  Building %APP% %VERSION% ^(%RID%, self-contained^) ===
if exist "%OUT%" rmdir /s /q "%OUT%"
if exist "%ZIP%" del /q "%ZIP%"
dotnet publish src\EasySearch\EasySearch.csproj -c Release -r %RID% --self-contained true ^
    -p:PublishReadyToRun=true -p:DebugType=none -o "%OUT%" --nologo -v minimal
if errorlevel 1 (
    echo [error] Publish failed - nothing was published.
    exit /b 1
)
if not exist "%OUT%\%APP%.exe" (
    echo [error] Expected "%OUT%\%APP%.exe" was not produced.
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -CompressionLevel Optimal"
if errorlevel 1 (
    echo [error] Could not create the release ZIP.
    exit /b 1
)
echo Release ZIP: "%ZIP%"
set "BUILD_ID="
for /f "delims=" %%B in ('powershell -NoProfile -Command "(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText('%OUT%\update-manifest.json'))).buildId"') do set "BUILD_ID=%%B"
if not defined BUILD_ID (
    echo [error] The release build identity is missing.
    exit /b 1
)

set "DIRTY="
for /f "delims=" %%S in ('git status --porcelain 2^>nul') do set "DIRTY=1"
if defined DIRTY echo [note] This build includes uncommitted changes from the working tree.

rem --- GitHub repository (first run only) -------------------------------------
echo.
echo === 3/4  GitHub repository ===
git remote get-url origin >nul 2>&1
if errorlevel 1 (
    set "BRANCH="
    for /f "delims=" %%B in ('git branch --show-current') do set "BRANCH=%%B"
    if not defined BRANCH (
        echo [error] Git is not on a branch - check out main and try again.
        exit /b 1
    )
    set "OWNER="
    for /f "delims=" %%U in ('call "%GH%" api user --jq .login') do set "OWNER=%%U"
    echo This folder is not connected to a GitHub repository yet.
    choice /c PR /n /m "Create github.com/!OWNER!/%APP% as [P]ublic or p[R]ivate? "
    set "VISIBILITY=--public"
    if errorlevel 2 set "VISIBILITY=--private"
    call "%GH%" repo create %APP% !VISIBILITY! --source "%~dp0." --remote origin --description "Instant file and folder search for Windows, in a frosted-glass command palette."
    if errorlevel 1 (
        echo [error] Could not create the GitHub repository.
        exit /b 1
    )
    git push -u origin !BRANCH!
    if errorlevel 1 (
        echo [error] Could not push the source to GitHub.
        exit /b 1
    )
) else (
    echo Using the existing origin remote.
)

rem --- publish ----------------------------------------------------------------
echo.
echo === 4/4  Uploading to GitHub release v%VERSION% ===
call "%GH%" release view v%VERSION% >nul 2>&1
if errorlevel 1 (
    call "%GH%" release create v%VERSION% "%ZIP%#build:%BUILD_ID%" --title "%APP% %VERSION%" --notes "Portable Windows x64 build of %APP% %VERSION%. Unzip it and run %APP%.exe. Needs Windows 10 or 11 with the WebView2 runtime, which is already part of Windows 11."
) else (
    call "%GH%" release upload v%VERSION% "%ZIP%#build:%BUILD_ID%" --clobber
)
if errorlevel 1 (
    echo [error] Upload to GitHub failed.
    exit /b 1
)

echo.
echo Published %APP% %VERSION%:
call "%GH%" release view v%VERSION% --json url --jq .url
exit /b 0
