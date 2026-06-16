<#
Build the WPF project, stop any running instance, and run the exe.
Usage: .\run-wpf.ps1 [-Wait]
If -Wait is specified, the script will run the process in foreground and wait for exit.
#>
param(
    [switch]$Wait
)

# Work from the repository root (script is in scripts\)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Join-Path $scriptDir ".."
Set-Location $repoRoot

$proj = "src/Gplx.WpfApp/Gplx.WpfApp.csproj"
Write-Host "Building project $proj (Debug)..."
$b = dotnet build $proj -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit $LASTEXITCODE
}

Write-Host "Stopping any running Gplx.WpfApp instances..."
Get-Process -Name "Gplx.WpfApp" -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }

$exe = Join-Path (Get-Location) "src\Gplx.WpfApp\bin\Debug\net8.0-windows\Gplx.WpfApp.exe"
if (-Not (Test-Path $exe)) {
    Write-Error "Executable not found: $exe"
    exit 2
}

Write-Host "Launching $exe"
if ($Wait) {
    & "$exe"
} else {
    Start-Process -FilePath "$exe"
}

Write-Host "Done."
