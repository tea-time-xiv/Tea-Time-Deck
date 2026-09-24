<#
.SYNOPSIS
    Reads or changes the game's volume channels, the way a Stream Deck + dial does.

.DESCRIPTION
    With no change, lists every channel from the status snapshot. Otherwise sends one
    volume.set and prints what the game now has. Watch System Configuration -> Sound
    Settings while running it: the slider there is the thing being moved.

.EXAMPLE
    .\tools\Set-Volume.ps1

.EXAMPLE
    .\tools\Set-Volume.ps1 -Channel bgm -Delta -10

.EXAMPLE
    .\tools\Set-Volume.ps1 -Channel master -Mute
#>
[CmdletBinding()]
param(
    [string]$Channel,

    [Nullable[int]]$Volume,

    [Nullable[int]]$Delta,

    # Switches rather than a [bool]: `powershell -File` hands every argument over as a
    # string, and a string will not bind to a boolean parameter.
    [switch]$Mute,

    [switch]$Unmute,

    [int]$Port = 37985
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TeaTimeDeck.psm1') -Force

$session = Connect-TeaTimeDeck -Port $Port
try {
    if (-not $Channel) {
        $status = Invoke-TeaTimeDeckRequest -Session $session -Type 'status.get'
        if (-not $status.ok) { throw $status.error }

        if (-not $status.payload.volume) {
            Write-Host 'this server reports no volume channels' -ForegroundColor Yellow
            exit 1
        }

        $status.payload.volume | Format-Table channel, volume, muted -AutoSize
        return
    }

    $payload = @{ channel = $Channel }
    if ($null -ne $Volume) { $payload.volume = $Volume }
    if ($null -ne $Delta) { $payload.delta = $Delta }
    if ($Mute -and $Unmute) { throw 'pick one of -Mute and -Unmute' }
    if ($Mute) { $payload.muted = $true }
    if ($Unmute) { $payload.muted = $false }

    $response = Invoke-TeaTimeDeckRequest -Session $session -Type 'volume.set' -Payload $payload

    if (-not $response.ok) {
        Write-Host "refused: $($response.error)" -ForegroundColor Red
        exit 1
    }

    $p = $response.payload
    Write-Host ("{0} = {1}  muted={2}" -f $p.channel, $p.volume, $p.muted) -ForegroundColor Green
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}
