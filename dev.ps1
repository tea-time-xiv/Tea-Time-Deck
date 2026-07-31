# Dot-source this to opt the current shell into a portable .NET 10 SDK:
#   . .\dev.ps1
# Only affects THIS shell. Needed when .NET 10 is not installed machine-wide --
# see BUILDING.md. Set DOTNET10_ROOT first if your install is not in the default
# location.
if (-not $env:DOTNET10_ROOT) {
    $env:DOTNET10_ROOT = Join-Path $env:USERPROFILE 'dotnet10'
}

if (-not (Test-Path (Join-Path $env:DOTNET10_ROOT 'dotnet.exe'))) {
    Write-Host "No dotnet.exe under $env:DOTNET10_ROOT." -ForegroundColor Yellow
    Write-Host "Set DOTNET10_ROOT to your portable SDK, or see BUILDING.md to install one." -ForegroundColor Yellow
    return
}

$env:DOTNET_ROOT = $env:DOTNET10_ROOT
$env:PATH = "$env:DOTNET10_ROOT;$env:PATH"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
Write-Host "Portable .NET SDK active for this shell:" -ForegroundColor Green
dotnet --version
