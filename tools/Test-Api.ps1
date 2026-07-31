<#
.SYNOPSIS
    Smoke-tests the TeaTimeDeck local API without needing the Stream Deck half.

.DESCRIPTION
    Connects to the in-game plugin, runs the handshake, and checks that each catalog
    returns entries. Requires FFXIV running with TeaTimeDeck loaded.

.EXAMPLE
    .\tools\Test-Api.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 37985
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TeaTimeDeck.psm1') -Force

$health = Test-TeaTimeDeckHealth -Port $Port
Write-Host "health: $($health.server) protocol $($health.protocolVersion)" -ForegroundColor Green

$session = Connect-TeaTimeDeck -Port $Port
Write-Host "connected" -ForegroundColor Green

try {
    $hello = Invoke-TeaTimeDeckRequest -Session $session -Type 'hello'
    Write-Host ("hello:  {0} v{1}, logged in as {2}" -f
        $hello.payload.server, $hello.payload.pluginVersion, $hello.payload.characterName)

    $ping = Invoke-TeaTimeDeckRequest -Session $session -Type 'ping'
    Write-Host "ping:   $($ping.payload.pong)"

    # An unknown request must come back as an error response, not a dropped connection.
    $bad = Invoke-TeaTimeDeckRequest -Session $session -Type 'no-such-request'
    if ($bad.ok) { throw "expected an error response for an unknown request type" }
    Write-Host "error handling: $($bad.error)"

    $kinds = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.kinds'
    Write-Host ""
    Write-Host "catalogs:" -ForegroundColor Cyan

    foreach ($kind in $kinds.payload.kinds) {
        $list = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.list' -Payload @{ kind = $kind.kind }

        if (-not $list.ok) {
            Write-Host ("  {0,-8} FAILED: {1}" -f $kind.kind, $list.error) -ForegroundColor Red
            continue
        }

        $colour = if ($list.payload.count -gt 0) { 'Green' } else { 'Yellow' }
        Write-Host ("  {0,-8} {1,5} entries  ({2})" -f
            $kind.kind, $list.payload.count, $kind.displayName) -ForegroundColor $colour

        $list.payload.entries | Select-Object -First 3 | ForEach-Object {
            Write-Host ("           e.g. {0} (id {1}, icon {2}, {3})" -f
                $_.name, $_.id, $_.iconId, $_.category) -ForegroundColor DarkGray
        }
    }

    Write-Host ""
    Write-Host "ok" -ForegroundColor Green
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}
