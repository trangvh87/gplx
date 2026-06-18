@echo off
rem Build the WPF project, kill any running instance, then launch the built exe
setlocal

rem Determine repository root relative to this script (script is in scripts\)
set SCRIPT_DIR=%~dp0
rem Remove trailing backslash
if "%SCRIPT_DIR:~-1%"=="\" set SCRIPT_DIR=%SCRIPT_DIR:~0,-1%
set REPO_ROOT=%SCRIPT_DIR%\..

pushd "%REPO_ROOT%" >nul || (
  echo Failed to change directory to repository root: %REPO_ROOT%
  exit /b 1
)

echo Building Gplx.WpfApp (Debug)...
dotnet build src\Gplx.WpfApp\Gplx.WpfApp.csproj -c Debug
if errorlevel 1 (
  echo Build failed.
  popd >nul
  exit /b 1
)

echo Stopping any running Gplx.WpfApp.exe instances...
taskkill /f /im Gplx.WpfApp.exe >nul 2>&1

set EXE_PATH=%CD%\src\Gplx.WpfApp\bin\Debug\net48\Gplx.WpfApp.exe
if exist "%EXE_PATH%" (
  echo Launching %EXE_PATH% ...
  start "" "%EXE_PATH%"
  echo Launched.
  popd >nul
  exit /b 0
) else (
  echo Executable not found: %EXE_PATH%
  popd >nul
  exit /b 2
)

endlocal
