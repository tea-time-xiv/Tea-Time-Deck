<#
.SYNOPSIS
    Dumps one TeaTimeDeck catalog, for eyeballing what the deck browser will show.

.EXAMPLE
    .\tools\Get-Catalog.ps1 -Kind emote

.EXAMPLE
    # Everything you own, grouped the way the browser will page it
    .\tools\Get-Catalog.ps1 -Kind mount | Group-Object category |
        Select-Object Count, Name

.EXAMPLE
    .\tools\Get-Catalog.ps1 -Kind emote | Export-Csv emotes.csv -NoTypeInformation
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Kind,

    [int]$Port = 37985
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TeaTimeDeck.psm1') -Force

$session = Connect-TeaTimeDeck -Port $Port
try {
    $response = Invoke-TeaTimeDeckRequest -Session $session -Type 'catalog.list' -Payload @{ kind = $Kind }

    if (-not $response.ok) {
        throw $response.error
    }

    # Straight to the pipeline so callers can sort, group, filter or export.
    $response.payload.entries
}
finally {
    Disconnect-TeaTimeDeck -Session $session
}
