@echo off
rem ---------------------------------------------------------------------------
rem  Build EasySearch from the current source, install it for this user and start it.
rem  Installs to %LOCALAPPDATA%\Programs\EasySearch (self-contained win-x64, so it
rem  does not need the .NET SDK to run) and adds a Start Menu shortcut.
rem  Settings and the index are not touched: they stay in %APPDATA%\EasySearch.
rem ---------------------------------------------------------------------------
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "APP=EasySearch"
set "RID=win-x64"
set "INSTALL=%LOCALAPPDATA%\Programs\%APP%"
set "STAGE=%~dp0artifacts\install-%RID%"
set "SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\%APP%.lnk"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [error] The .NET SDK was not found. Install the .NET 10 SDK from https://dot.net and try again.
    goto :fail
)

rem --- version, read from the one place it is defined -------------------------
set "VERSION="
for /f "delims=" %%V in ('powershell -NoProfile -Command "([xml](Get-Content -Raw Directory.Build.props)).SelectSingleNode('//Version').InnerText.Trim()"') do if not defined VERSION set "VERSION=%%V"
if not defined VERSION set "VERSION=unknown"

rem --- stop the running copy, it locks its own files ---------------------------
echo === 1/4  Stopping the running copy ===
taskkill /F /IM %APP%.exe >nul 2>&1
for /l %%I in (1,1,20) do (
    tasklist /FI "IMAGENAME eq %APP%.exe" 2>nul | find /I "%APP%.exe" >nul
    if errorlevel 1 goto :stopped
    ping -n 2 127.0.0.1 >nul
)
echo [error] %APP% is still running and could not be stopped.
goto :fail
:stopped

rem --- build -------------------------------------------------------------------
echo.
echo === 2/4  Building %APP% %VERSION% ^(%RID%, self-contained^) ===
if exist "%STAGE%" rmdir /s /q "%STAGE%"
dotnet publish src\%APP%\%APP%.csproj -c Release -r %RID% --self-contained true ^
    -p:PublishReadyToRun=true -p:DebugType=none -o "%STAGE%" --nologo -v minimal
if errorlevel 1 (
    echo [error] Build failed - the installed copy was left untouched.
    goto :fail
)
if not exist "%STAGE%\%APP%.exe" (
    echo [error] Expected "%STAGE%\%APP%.exe" was not produced.
    goto :fail
)

rem --- install -----------------------------------------------------------------
echo.
echo === 3/4  Installing to %INSTALL% ===
if not exist "%INSTALL%" mkdir "%INSTALL%"
robocopy "%STAGE%" "%INSTALL%" /MIR /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
    echo [error] Could not copy the build to "%INSTALL%".
    goto :fail
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('%SHORTCUT%');" ^
    "$s.TargetPath = '%INSTALL%\%APP%.exe';" ^
    "$s.WorkingDirectory = '%INSTALL%';" ^
    "$s.IconLocation = '%INSTALL%\%APP%.exe,0';" ^
    "$s.Description = 'Instant file and folder search';" ^
    "$s.Save()"
if errorlevel 1 echo [warn] Could not create the Start Menu shortcut.

rem --- run ---------------------------------------------------------------------
echo.
echo === 4/4  Starting %APP% ===
start "" "%INSTALL%\%APP%.exe" --background
ping -n 4 127.0.0.1 >nul
tasklist /FI "IMAGENAME eq %APP%.exe" 2>nul | find /I "%APP%.exe" >nul
if errorlevel 1 (
    echo [error] %APP% did not stay running. Start "%INSTALL%\%APP%.exe" to see the error.
    goto :fail
)

echo.
echo Installed %APP% %VERSION% to "%INSTALL%" and started it.
echo Press Win+Space to open EasySearch. It is also in the Start Menu.
exit /b 0

:fail
echo.
pause
exit /b 1
