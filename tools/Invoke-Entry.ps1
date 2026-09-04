<#
.SYNOPSIS
    Executes one catalog entry in-game. Your character will visibly do this.

.DESCRIPTION
    Proves the executors without the Stream Deck half. Identify the entry by id, by
    key for the kinds the game does not number (Glamourer designs), or by name for
    convenience -- which works for every kind and resolves to whichever it uses.

.EXAMPLE
    .\tools\Invoke-Entry.ps1 -Kind emote -Name Wave

.EXAMPLE
    .\tools\Invoke-Entry.ps1 -Kind mount -Id 1

.EXAMPLE
    .\tools\Invoke-Entry.ps1 -Kind glamourer -Name "Elezen F"
#>
[CmdletBinding(DefaultParameterSetName = 'ById')]
param(
    [Parameter(Mandatory = $true)]
    [string]$Kind,

    [Parameter(Mandatory = $true, ParameterSetName = 'ById')]
    [uint32]$Id,

    [Parameter(Mandatory = $true, ParameterSetName = 'ByKey')]
    [string]$Key,

    [Parameter(Mandatory = $true, ParameterSetName = 'ByName')]
    [string]$Name,

    [int]$Port = 37985
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TeaTimeDeck.psm1') -Force

$session = Connect-TeaTimeDeck -Port $Port
try {
    if ($PSCmdlet.ParameterSetName -eq 'ByName') {
        $list = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.list' -Payload @{ kind = $Kind }
        if (-not $list.ok) { throw $list.error }

        $matched = @($list.payload.entries | Where-Object { $_.name -like $Name })
        if ($matched.Count -eq 0) { throw "no $Kind matching '$Name'" }
        if ($matched.Count -gt 1) {
            throw "'$Name' matches $($matched.Count): $(($matched | Select-Object -First 8 -Expand name) -join ', ')"
        }

        # A design has no row id, so which of the two came back is what identifies it.
        if ($matched[0].key) {
            $Key = $matched[0].key
            Write-Host "resolved '$Name' -> $($matched[0].name) (key $Key)" -ForegroundColor DarkGray
        }
        else {
            $Id = $matched[0].id
            Write-Host "resolved '$Name' -> $($matched[0].name) (id $Id)" -ForegroundColor DarkGray
        }
    }

    $payload = if ($Key) { @{ kind = $Kind; key = $Key } } else { @{ kind = $Kind; id = $Id } }
    $response = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload $payload

    if (-not $response.ok) {
        Write-Host "refused: $($response.error)" -ForegroundColor Red
        exit 1
    }

    $p = $response.payload
    Write-Host ("executed {0} {1} ({2})  usable={3} result={4}" -f
        $p.kind, $(if ($p.key) { $p.key } else { $p.id }), $p.name, $p.usable, $p.result) -ForegroundColor Green
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}
