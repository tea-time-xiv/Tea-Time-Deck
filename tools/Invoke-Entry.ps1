<#
.SYNOPSIS
    Executes one catalog entry in-game. Your character will visibly do this.

.DESCRIPTION
    Proves the hotbar executor without the Stream Deck half. Identify the entry by
    id, or by name for convenience.

.EXAMPLE
    .\tools\Invoke-Entry.ps1 -Kind emote -Name Wave

.EXAMPLE
    .\tools\Invoke-Entry.ps1 -Kind mount -Id 1
#>
[CmdletBinding(DefaultParameterSetName = 'ById')]
param(
    [Parameter(Mandatory = $true)]
    [string]$Kind,

    [Parameter(Mandatory = $true, ParameterSetName = 'ById')]
    [uint32]$Id,

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

        $Id = $matched[0].id
        Write-Host "resolved '$Name' -> $($matched[0].name) (id $Id)" -ForegroundColor DarkGray
    }

    $response = Invoke-TeaTimeDeckRequest -Session $session -Type 'execute' -Payload @{ kind = $Kind; id = $Id }

    if (-not $response.ok) {
        Write-Host "refused: $($response.error)" -ForegroundColor Red
        exit 1
    }

    $p = $response.payload
    Write-Host ("executed {0} {1} ({2})  usable={3} result={4}" -f
        $p.kind, $p.id, $p.name, $p.usable, $p.result) -ForegroundColor Green
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}
