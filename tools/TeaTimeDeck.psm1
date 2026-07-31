<#
    Minimal TeaTimeDeck API client for PowerShell, so the game half can be exercised
    without the Stream Deck half. Import it, don't run it:

        Import-Module .\tools\TeaTimeDeck.psm1
#>

function New-TeaTimeDeckToken {
    param([int]$TimeoutSeconds)
    [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
}

function Test-TeaTimeDeckHealth {
    <#
    .SYNOPSIS
        Unauthenticated liveness probe. Distinguishes "plugin not loaded" from "bad key".
    #>
    [CmdletBinding()]
    param([int]$Port = 37985, [int]$TimeoutSeconds = 5)

    try {
        Invoke-RestMethod -Uri "http://localhost:$Port/health" -TimeoutSec $TimeoutSeconds
    }
    catch {
        throw "No TeaTimeDeck listening on port $Port. Is FFXIV running with the plugin enabled? ($_)"
    }
}

function Connect-TeaTimeDeck {
    [CmdletBinding()]
    param(
        [int]$Port = 37985,
        [int]$TimeoutSeconds = 30
    )

    $socket = [System.Net.WebSockets.ClientWebSocket]::new()

    # A per-operation token, not a shared one: a single long-lived CTS with a timeout
    # would expire mid-session and kill every later request.
    $cts = New-TeaTimeDeckToken $TimeoutSeconds
    try {
        [void]$socket.ConnectAsync("ws://localhost:$Port/ws", $cts.Token).GetAwaiter().GetResult()
    }
    catch {
        $socket.Dispose()
        throw "Connect failed. A 403 means the request carried an Origin header, which the plugin refuses. ($_)"
    }
    finally {
        $cts.Dispose()
    }

    [pscustomobject]@{
        Socket         = $socket
        TimeoutSeconds = $TimeoutSeconds
    }
}

function Invoke-TeaTimeDeckRequest {
    <#
    .SYNOPSIS
        Sends one request and returns the matching response.
    .DESCRIPTION
        Reassembles fragmented frames -- a catalog listing is far larger than one frame --
        and skips server-pushed events until the reply with our correlation id arrives.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][string]$Type,
        [hashtable]$Payload
    )

    $id = [guid]::NewGuid().ToString('N')
    $body = @{ id = $id; type = $Type }
    if ($Payload) { $body.payload = $Payload }

    $cts = New-TeaTimeDeckToken $Session.TimeoutSeconds
    try {
        $json = $body | ConvertTo-Json -Compress -Depth 10
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        [void]$Session.Socket.SendAsync(
            [System.ArraySegment[byte]]::new($bytes), 'Text', $true, $cts.Token).GetAwaiter().GetResult()

        while ($true) {
            $stream = [System.IO.MemoryStream]::new()
            try {
                $buffer = [byte[]]::new(65536)
                do {
                    $result = $Session.Socket.ReceiveAsync(
                        [System.ArraySegment[byte]]::new($buffer), $cts.Token).GetAwaiter().GetResult()
                    $stream.Write($buffer, 0, $result.Count)
                } while (-not $result.EndOfMessage)

                $message = [System.Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            }
            finally {
                $stream.Dispose()
            }

            if ($message.id -eq $id) { return $message }

            Write-Verbose "unsolicited event: $($message.type)"
        }
    }
    finally {
        $cts.Dispose()
    }
}

function Disconnect-TeaTimeDeck {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Session)

    $cts = New-TeaTimeDeckToken 5
    try {
        if ($Session.Socket.State -eq 'Open') {
            [void]$Session.Socket.CloseAsync('NormalClosure', 'done', $cts.Token).GetAwaiter().GetResult()
        }
    }
    catch {
        Write-Verbose "close failed: $_"
    }
    finally {
        $cts.Dispose()
        $Session.Socket.Dispose()
    }
}

Export-ModuleMember -Function Test-TeaTimeDeckHealth, Connect-TeaTimeDeck, Invoke-TeaTimeDeckRequest, Disconnect-TeaTimeDeck
