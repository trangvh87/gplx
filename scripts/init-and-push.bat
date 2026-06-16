@echo off
rem Initialize git repo, commit all files and push to GitHub.
rem Usage: run from anywhere; script will operate relative to repo root (script is in scripts\)

setlocal
set SCRIPT_DIR=%~dp0
if "%SCRIPT_DIR:~-1%"=="\" set SCRIPT_DIR=%SCRIPT_DIR:~0,-1%
set REPO_ROOT=%SCRIPT_DIR%\..

pushd "%REPO_ROOT%" >nul || (
  echo Failed to change directory to repository root: %REPO_ROOT%
  exit /b 1
)

where git >nul 2>&1
if errorlevel 1 (
  echo Git is not installed or not on PATH. Please install Git: https://git-scm.com/downloads
  popd >nul
  exit /b 2
)

echo Initializing git repository (if not already)...
git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
  git init
) else (
  echo Already a git repository.
)

echo Setting main branch name to 'main'
git branch -M main 2>nul

echo Adding files...
git add .

echo Committing...
git commit -m "Initial commit" 2>nul || (
  echo No changes to commit or commit failed.
)

rem Try to use gh to create remote repo if available
where gh >nul 2>&1
if %ERRORLEVEL%==0 (
  echo GitHub CLI detected. Creating repository on GitHub if needed...
  rem Default to repository name as current folder name
  for %%I in (.) do set REPO_NAME=%%~nI
  gh repo create %REPO_NAME% --public --source=. --remote=origin --push --confirm || (
    echo gh repo create failed or repo already exists; ensure remote exists.
  )
  popd >nul
  exit /b 0
) else (
  echo GitHub CLI (gh) not found. Please provide remote URL to push.
  set /p REMOTE_URL=Enter remote URL (e.g. https://github.com/user/repo.git): 
  if "%REMOTE_URL%"=="" (
    echo No remote provided. Aborting.
    popd >nul
    exit /b 3
  )
  git remote add origin %REMOTE_URL% 2>nul || echo Remote 'origin' already exists.
  echo Pushing to origin main...
  git push -u origin main
  popd >nul
  exit /b 0
)

endlocal
