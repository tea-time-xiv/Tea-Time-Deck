<#
.SYNOPSIS
    Exercises the Glamourer design catalog and every way of getting it wrong.

.DESCRIPTION
    The point of most of these is not that a request succeeds but that a bad one is
    refused with a sentence and the plugin carries on -- so each failing case is
    followed by a live check that the socket and the port are still there.

    Runs against both states on purpose. With Glamourer loaded it checks the list, the
    pinned Reset entry the plugin mints itself and, with -Apply, that a design really
    goes on and that Reset takes it back off. With Glamourer disabled in /xlplugins it
    checks the part that is easy to get wrong: an empty catalog rather than an error, no
    Reset key left behind, and a refusal rather than a crash. Run it once each way.

.EXAMPLE
    .\tools\Test-Glamourer.ps1

.EXAMPLE
    .\tools\Test-Glamourer.ps1 -Apply

.EXAMPLE
    .\tools\Test-Glamourer.ps1 -Apply -Design "Hume F"
#>
[CmdletBinding()]
param(
    [int]$Port = 37985,

    # Applies a design for real. Your character visibly changes; Glamourer reverts it.
    [switch]$Apply,

    [string]$Design,

    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TeaTimeDeck.psm1') -Force

$script:Passed = 0
$script:Failed = 0

function Assert-That {
    param([string]$What, [bool]$Condition, [string]$Detail)

    if ($Condition) {
        $script:Passed++
        Write-Host ("  PASS  {0}" -f $What) -ForegroundColor Green
    }
    else {
        $script:Failed++
        Write-Host ("  FAIL  {0}" -f $What) -ForegroundColor Red
    }

    if ($Detail) { Write-Host ("        {0}" -f $Detail) -ForegroundColor DarkGray }
}

# Every refusal is followed by this: an error response is only the right behaviour if the
# plugin is still standing afterwards.
function Assert-StillAlive {
    param($Session, [string]$After)

    $alive = $false
    try {
        $ping = Invoke-TeaTimeDeckRequest -Session $Session -Type 'ping'
        $probe = Test-TeaTimeDeckHealth -Port $Port
        $alive = $ping.ok -and $probe.server -eq 'TeaTimeDeck'
    }
    catch {
        $alive = $false
    }

    Assert-That "still serving after $After" $alive
}

$health = Test-TeaTimeDeckHealth -Port $Port
Write-Host "health: $($health.server) protocol $($health.protocolVersion)" -ForegroundColor Cyan

$session = Connect-TeaTimeDeck -Port $Port -TimeoutSeconds $TimeoutSeconds
try {
    Write-Host ""
    Write-Host "catalog" -ForegroundColor Cyan

    $kinds = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.kinds'
    $glamourer = $kinds.payload.kinds | Where-Object { $_.kind -eq 'glamourer' }

    Assert-That "glamourer is listed whether or not Glamourer is loaded" ($null -ne $glamourer)
    Assert-That "glamourer is addressed by key" ($glamourer.addressing -eq 'key') ("addressing = {0}" -f $glamourer.addressing)

    $idKinds = @($kinds.payload.kinds | Where-Object { $_.kind -ne 'glamourer' })
    $allById = @($idKinds | Where-Object { $_.addressing -ne 'id' }).Count -eq 0
    Assert-That "every other kind is still addressed by id" $allById

    $list = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.list' -Payload @{ kind = 'glamourer' }
    Assert-That "catalog.list glamourer answers rather than failing" ([bool]$list.ok) $list.error

    $entries = @($list.payload.entries)
    $loaded = $entries.Count -gt 0

    # Reset is minted by the plugin rather than listed by Glamourer, so it is the one entry
    # that is not a design and fails every assertion below about being one.
    $reset = $entries | Where-Object { $_.key -eq 'reset' } | Select-Object -First 1
    $designs = @($entries | Where-Object { $_.key -ne 'reset' })

    if ($loaded) {
        Write-Host ("  ..    Glamourer is loaded: {0} designs plus Reset" -f $designs.Count) -ForegroundColor DarkGray

        Assert-That "Reset is in the catalog" ($null -ne $reset)
        if ($reset) {
            Assert-That "Reset is first, pinned, and says what it does" `
                (($entries[0].key -eq 'reset') -and ($reset.pinned -eq $true) -and ($reset.command -like '/glamour revert*')) `
                ("first={0} pinned={1} command={2}" -f $entries[0].key, $reset.pinned, $reset.command)
        }

        $shaped = $true
        $reason = ''
        foreach ($entry in $designs) {
            $parsed = [guid]::Empty
            if (-not [guid]::TryParse($entry.key, [ref]$parsed)) {
                $shaped = $false; $reason = "key '$($entry.key)' is not a GUID"; break
            }
            if ($entry.id -ne 0) {
                $shaped = $false; $reason = "$($entry.name) has id $($entry.id), expected 0"; break
            }
            if ($entry.iconId -ne 0) {
                $shaped = $false; $reason = "$($entry.name) has iconId $($entry.iconId), expected 0"; break
            }
            if ($entry.command -notlike '/glamour apply*') {
                $shaped = $false; $reason = "$($entry.name) reports '$($entry.command)'"; break
            }
        }

        Assert-That "every design carries a GUID key, no id and no icon" $shaped $reason
    }
    else {
        Write-Host "  ..    nothing came back -- Glamourer is not loaded" -ForegroundColor Yellow
        Assert-That "an absent Glamourer is an empty list, not an error" ([bool]$list.ok)

        # Reset reverts over the same IPC as an apply, so it has to leave with Glamourer
        # rather than sit there as the one pressable key in an otherwise empty type.
        Assert-That "Reset is gone with Glamourer, not left behind" ($null -eq $reset)

        $resetGone = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = 'reset' }
        Assert-That "pressing a remembered Reset key is refused" ((-not $resetGone.ok) -and $resetGone.error -like '*not in your catalog*') $resetGone.error
        Assert-StillAlive -Session $session -After 'a Reset with no Glamourer'
    }

    Write-Host ""
    Write-Host "refusals" -ForegroundColor Cyan

    # A design that is not in the catalog cannot be applied, however well-formed its id.
    # This is the path a deck key takes after its design is deleted, and the one it takes
    # after Glamourer is unloaded with that key still sitting on the deck.
    $unknown = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = [guid]::NewGuid().ToString() }
    Assert-That "a key that is not in the catalog is refused" ((-not $unknown.ok) -and $unknown.error -like '*not in your catalog*') $unknown.error
    Assert-StillAlive -Session $session -After 'an unknown key'

    $notAGuid = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = 'not-a-guid' }
    Assert-That "a key that is not a GUID at all is refused" (-not $notAGuid.ok) $notAGuid.error

    $wrongField = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; id = 3 }
    Assert-That "an id where a key belongs is refused" ((-not $wrongField.ok) -and $wrongField.error -like '*key*') $wrongField.error

    $noPayload = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute'
    Assert-That "an execute with no payload is refused" (-not $noPayload.ok) $noPayload.error

    $unknownKind = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'ornament'; id = 3 }
    Assert-That "a kind that does not exist is refused" ((-not $unknownKind.ok) -and $unknownKind.error -like '*unknown catalog kind*') $unknownKind.error
    Assert-StillAlive -Session $session -After 'an unknown kind'

    # Twenty refusals back to back: none of them may take the connection down, and none of
    # them may spend the interval floor, since none of them reached the game.
    $refused = 0
    for ($i = 0; $i -lt 20; $i++) {
        $burst = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = [guid]::NewGuid().ToString() }
        if (-not $burst.ok) { $refused++ }
    }

    Assert-That "twenty bad executes are all refused" ($refused -eq 20) "$refused of 20"
    Assert-StillAlive -Session $session -After 'a burst of bad executes'

    if ($Apply -and $designs.Count -gt 0) {
        Write-Host ""
        Write-Host "applying (your character will change)" -ForegroundColor Cyan

        $target = $designs[0]
        if ($Design) {
            $target = $designs | Where-Object { $_.name -like $Design } | Select-Object -First 1
            if (-not $target) { throw "no design matching '$Design'" }
        }

        $applied = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = $target.key }
        Assert-That ("'{0}' applies" -f $target.name) ($applied.ok -and $applied.payload.result -eq 0) ("ok={0} result={1} {2}" -f $applied.ok, $applied.payload.result, $applied.error)

        # The floor is shared with the hotbar path, so this is what would catch a second
        # executor quietly counting an interval of its own.
        $tooFast = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = $target.key }
        Assert-That "a second press within 100 ms is refused" ((-not $tooFast.ok) -and $tooFast.error -like '*too fast*') $tooFast.error

        Start-Sleep -Milliseconds 200

        $again = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = $target.key }
        Assert-That "and allowed once the floor has passed" ([bool]$again.ok) $again.error

        Start-Sleep -Milliseconds 200

        # Watch the character here: this is the press that has to put back what the two
        # above put on. Glamourer answers 0 for a revert that did something.
        $undone = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = 'reset' }
        Assert-That "Reset takes the design back off" ($undone.ok -and $undone.payload.result -eq 0) ("ok={0} result={1} {2}" -f $undone.ok, $undone.payload.result, $undone.error)

        Start-Sleep -Milliseconds 200

        # Reverting a character with nothing on is not an error. Glamourer answers Success
        # rather than NothingDone for it (measured, 1.6.1.7); both are accepted, because
        # which one comes back is Glamourer's business and a red key would be lying either way.
        $twice = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = 'glamourer'; key = 'reset' }
        Assert-That "a second Reset succeeds with nothing to do" ($twice.ok -and $twice.payload.result -in 0, 1) ("ok={0} result={1} {2}" -f $twice.ok, $twice.payload.result, $twice.error)
    }
    elseif ($Apply) {
        Write-Host ""
        Write-Host "  ..    -Apply skipped: there are no designs to apply" -ForegroundColor Yellow
    }
    else {
        Write-Host ""
        Write-Host "  ..    pass -Apply to also put a design on your character" -ForegroundColor DarkGray
    }

    Write-Host ""
    if ($loaded) {
        Write-Host "Ran against a loaded Glamourer. Disable it in /xlplugins and run this again to" -ForegroundColor Yellow
        Write-Host "cover the other half: an empty catalog and a refusal, rather than a crash." -ForegroundColor Yellow
    }
    else {
        Write-Host "Ran against a Glamourer that is not loaded. Run it again with Glamourer enabled" -ForegroundColor Yellow
        Write-Host "to cover the listing and the apply." -ForegroundColor Yellow
    }

    Write-Host ""
    $colour = 'Green'
    if ($script:Failed -gt 0) { $colour = 'Red' }
    Write-Host ("{0} passed, {1} failed" -f $script:Passed, $script:Failed) -ForegroundColor $colour
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}

if ($script:Failed -gt 0) { exit 1 }
