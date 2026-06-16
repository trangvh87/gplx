@echo off
rem Push project to GitHub using installed git and gh (uses absolute paths)
setlocal

set SCRIPT_DIR=%~dp0
if "%SCRIPT_DIR:~-1%"=="\" set SCRIPT_DIR=%SCRIPT_DIR:~0,-1%
set REPO_ROOT=%SCRIPT_DIR%\..
pushd "%REPO_ROOT%" >nul || (
  echo Failed to change directory to repo root: %REPO_ROOT%
  exit /b 1
)

set GIT_EXE="C:\Program Files\Git\bin\git.exe"
set GH_EXE="C:\Program Files\GitHub CLI\gh.exe"

echo Using %GIT_EXE%
echo Using %GH_EXE%

%GIT_EXE% rev-parse --is-inside-work-tree >nul 2>&1 || %GIT_EXE% init
%GIT_EXE% branch -M main 2>nul

if not exist README.md (
  echo Creating README.md
  echo # Gplx>README.md
)

echo Adding files...
%GIT_EXE% add .

echo Committing...
%GIT_EXE% commit -m "Initial commit" 2>nul || echo No changes to commit or commit failed.

echo Creating GitHub repo and pushing (may prompt)...
%GH_EXE% repo create --public --source . --remote origin --push --confirm

popd >nul
endlocal
