<#
.SYNOPSIS
    Links the built Stream Deck plugin into the Stream Deck app for development.

.DESCRIPTION
    Creates a directory junction from the Stream Deck plugins folder to this repo, so a
    rebuild is picked up by restarting the app rather than reinstalling. A junction is
    used rather than a symlink because it needs no administrator rights.

    The Stream Deck app only scans for plugins at startup, so it must be restarted after
    linking, and again after each rebuild.

.EXAMPLE
    .\tools\Install-StreamDeckPlugin.ps1

.EXAMPLE
    .\tools\Install-StreamDeckPlugin.ps1 -Restart
#>
[CmdletBinding()]
param(
    # Close and relaunch the Stream Deck app once linked.
    [switch]$Restart,

    # Remove the link instead of creating it.
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$pluginId = 'xiv.teatime.deck.sdPlugin'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) "streamdeck\$pluginId"
$target = Join-Path $env:APPDATA "Elgato\StreamDeck\Plugins\$pluginId"

function Stop-StreamDeck {
    $processes = Get-Process -Name 'StreamDeck' -ErrorAction SilentlyContinue
    if (-not $processes) { return $false }

    $processes | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    return $true
}

if ($Uninstall) {
    if (Test-Path $target) {
        # Remove-Item on a junction deletes the link, not the linked-to content, but be
        # explicit about it rather than trusting that at the call site.
        [System.IO.Directory]::Delete($target, $true)
        Write-Host "unlinked $target" -ForegroundColor Green
    }
    else {
        Write-Host "nothing linked at $target" -ForegroundColor DarkGray
    }
    exit 0
}

if (-not (Test-Path (Join-Path $source 'bin\plugin.js'))) {
    throw "No build found. Run 'npm run build' in streamdeck/ first."
}

if (Test-Path $target) {
    $existing = Get-Item $target -Force

    if ($existing.LinkType -eq 'Junction') {
        Write-Host "already linked" -ForegroundColor DarkGray
    }
    else {
        throw "$target exists and is a real directory, not a link. Remove it by hand if you want to replace an installed copy."
    }
}
else {
    # The app must not be running while its plugin folder changes underneath it.
    $wasRunning = Stop-StreamDeck

    New-Item -ItemType Junction -Path $target -Target $source | Out-Null
    Write-Host "linked $target" -ForegroundColor Green
    Write-Host "     -> $source" -ForegroundColor DarkGray

    if ($wasRunning) { $Restart = $true }
}

if ($Restart) {
    Stop-StreamDeck | Out-Null

    $exe = 'C:\Program Files\Elgato\StreamDeck\StreamDeck.exe'
    if (-not (Test-Path $exe)) {
        Write-Host "Stream Deck not found at $exe; start it yourself." -ForegroundColor Yellow
        exit 0
    }

    Start-Process $exe
    Write-Host "restarted Stream Deck" -ForegroundColor Green
}
else {
    Write-Host "Restart the Stream Deck app to load the plugin." -ForegroundColor Yellow
}
