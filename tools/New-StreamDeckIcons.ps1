<#
.SYNOPSIS
    Generates the PNG assets the Stream Deck manifest references.

.DESCRIPTION
    Stream Deck will not load a plugin whose manifest points at missing images, and every
    icon needs an @2x sibling. Generating them keeps the sizes correct and the look
    consistent; re-run after changing the artwork below.

    Action icons are white-on-transparent, which is what the Stream Deck app expects for
    the actions list. Key images are the full-colour tile shown on the hardware.

.EXAMPLE
    .\tools\New-StreamDeckIcons.ps1
#>
[CmdletBinding()]
param(
    [string]$PluginRoot = (Join-Path $PSScriptRoot '..\streamdeck\xiv.teatime.deck.sdPlugin')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$background = [System.Drawing.Color]::FromArgb(255, 32, 38, 56)
$crystal = [System.Drawing.Color]::FromArgb(255, 205, 170, 110)
$white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$black = [System.Drawing.Color]::FromArgb(255, 0, 0, 0)

function New-Glyph {
    <#
        Returns the silhouette drawn on an icon. Everything is built from primitives so
        the shapes stay crisp at 20px as well as 288px.
    #>
    param(
        [single]$Size,
        [ValidateSet('crystal', 'cell', 'left', 'right', 'swap', 'heart', 'shield', 'flag', 'hourglass', 'ring')]
        [string]$Glyph
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $cx = $Size / 2
    $cy = $Size / 2

    switch ($Glyph) {
        'crystal' {
            # The shape FFXIV uses for just about everything.
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx, $Size * 0.10)
                    [System.Drawing.PointF]::new($cx + $Size * 0.28, $Size * 0.38)
                    [System.Drawing.PointF]::new($cx, $Size * 0.90)
                    [System.Drawing.PointF]::new($cx - $Size * 0.28, $Size * 0.38)
                ))
        }

        'cell' {
            # One cell: this key is a single window onto the catalog, and the artwork it
            # shows in use is the entry's own icon, so the placeholder stays quiet.
            $cell = $Size * 0.20
            $path.AddRectangle([System.Drawing.RectangleF]::new(
                    ($Size - $cell) / 2, ($Size - $cell) / 2, $cell, $cell))
        }

        'left' {
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.22, $cy)
                    [System.Drawing.PointF]::new($cx + $Size * 0.18, $cy - $Size * 0.28)
                    [System.Drawing.PointF]::new($cx + $Size * 0.18, $cy + $Size * 0.28)
                ))
        }

        'right' {
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx + $Size * 0.22, $cy)
                    [System.Drawing.PointF]::new($cx - $Size * 0.18, $cy - $Size * 0.28)
                    [System.Drawing.PointF]::new($cx - $Size * 0.18, $cy + $Size * 0.28)
                ))
        }

        'heart' {
            # Vitals. Two lobes over a point.
            $r = $Size * 0.17
            $path.AddEllipse([System.Drawing.RectangleF]::new($cx - $r * 2, $cy - $Size * 0.30, $r * 2, $r * 2))
            $path.AddEllipse([System.Drawing.RectangleF]::new($cx, $cy - $Size * 0.30, $r * 2, $r * 2))
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.34, $cy - $Size * 0.10)
                    [System.Drawing.PointF]::new($cx + $Size * 0.34, $cy - $Size * 0.10)
                    [System.Drawing.PointF]::new($cx, $cy + $Size * 0.32)
                ))
        }

        'shield' {
            # Job. A crest.
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx, $cy - $Size * 0.32)
                    [System.Drawing.PointF]::new($cx + $Size * 0.26, $cy - $Size * 0.18)
                    [System.Drawing.PointF]::new($cx + $Size * 0.20, $cy + $Size * 0.16)
                    [System.Drawing.PointF]::new($cx, $cy + $Size * 0.34)
                    [System.Drawing.PointF]::new($cx - $Size * 0.20, $cy + $Size * 0.16)
                    [System.Drawing.PointF]::new($cx - $Size * 0.26, $cy - $Size * 0.18)
                ))
        }

        'flag' {
            # Duty. A pennant on a staff.
            $path.AddRectangle([System.Drawing.RectangleF]::new($cx - $Size * 0.26, $cy - $Size * 0.32, $Size * 0.07, $Size * 0.64))
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.19, $cy - $Size * 0.30)
                    [System.Drawing.PointF]::new($cx + $Size * 0.30, $cy - $Size * 0.14)
                    [System.Drawing.PointF]::new($cx - $Size * 0.19, $cy + $Size * 0.02)
                ))
        }

        'hourglass' {
            # Ventures. Two opposed triangles pinched at the waist.
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.24, $cy - $Size * 0.30)
                    [System.Drawing.PointF]::new($cx + $Size * 0.24, $cy - $Size * 0.30)
                    [System.Drawing.PointF]::new($cx, $cy)
                ))
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.24, $cy + $Size * 0.30)
                    [System.Drawing.PointF]::new($cx + $Size * 0.24, $cy + $Size * 0.30)
                    [System.Drawing.PointF]::new($cx, $cy)
                ))
        }

        'ring' {
            # Recast. An annulus with a wedge missing, like a cooldown sweep in progress.
            # Two concentric ellipses; the default alternate fill mode hollows the middle.
            $outer = $Size * 0.31
            $inner = $Size * 0.19
            $path.AddEllipse([System.Drawing.RectangleF]::new($cx - $outer, $cy - $outer, $outer * 2, $outer * 2))
            $path.AddEllipse([System.Drawing.RectangleF]::new($cx - $inner, $cy - $inner, $inner * 2, $inner * 2))

            # The wedge: a triangle from the centre up and to the right, punched out by
            # the same alternate fill rule.
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx, $cy)
                    [System.Drawing.PointF]::new($cx, $cy - $Size * 0.40)
                    [System.Drawing.PointF]::new($cx + $Size * 0.40, $cy - $Size * 0.40)
                ))
        }

        'swap' {
            # Two arrowheads on separate rows pointing opposite ways: cycling between
            # kinds. They must not overlap, or they read as a single lightning bolt.
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx + $Size * 0.26, $cy - $Size * 0.20)
                    [System.Drawing.PointF]::new($cx - $Size * 0.06, $cy - $Size * 0.36)
                    [System.Drawing.PointF]::new($cx - $Size * 0.06, $cy - $Size * 0.04)
                ))
            $path.AddPolygon(@(
                    [System.Drawing.PointF]::new($cx - $Size * 0.26, $cy + $Size * 0.20)
                    [System.Drawing.PointF]::new($cx + $Size * 0.06, $cy + $Size * 0.36)
                    [System.Drawing.PointF]::new($cx + $Size * 0.06, $cy + $Size * 0.04)
                ))
        }
    }

    $path
}

function Save-Icon {
    param(
        [string]$Path,
        [int]$Size,
        [string]$Glyph = 'crystal',
        [switch]$Transparent,
        [System.Drawing.Color]$Tile = $background
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = 'AntiAlias'
        $graphics.Clear([System.Drawing.Color]::Transparent)

        if (-not $Transparent) {
            # Rounded tile, matching the radius Stream Deck rounds keys to.
            $radius = [single]($Size * 0.18)
            $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $d = $radius * 2
            $tilePath.AddArc(0, 0, $d, $d, 180, 90)
            $tilePath.AddArc($Size - $d, 0, $d, $d, 270, 90)
            $tilePath.AddArc($Size - $d, $Size - $d, $d, $d, 0, 90)
            $tilePath.AddArc(0, $Size - $d, $d, $d, 90, 90)
            $tilePath.CloseFigure()

            $fill = [System.Drawing.SolidBrush]::new($Tile)
            $graphics.FillPath($fill, $tilePath)
            $fill.Dispose()
            $tilePath.Dispose()
        }

        $shape = New-Glyph -Size $Size -Glyph $Glyph
        $colour = if ($Transparent) { $white } else { $crystal }
        $brush = [System.Drawing.SolidBrush]::new($colour)
        $graphics.FillPath($brush, $shape)
        $brush.Dispose()
        $shape.Dispose()

        $parent = Split-Path $Path -Parent
        if (-not (Test-Path $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("  {0,-46} {1}x{1}" -f (Split-Path $Path -Leaf), $Size)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$PluginRoot = (Resolve-Path $PluginRoot).Path
Write-Host "writing icons under $PluginRoot" -ForegroundColor Cyan

# Marketplace icon has no @2x variant.
Save-Icon -Path (Join-Path $PluginRoot 'imgs\plugin\marketplace.png') -Size 288

Save-Icon -Path (Join-Path $PluginRoot 'imgs\plugin\category-icon.png') -Size 28 -Transparent
Save-Icon -Path (Join-Path $PluginRoot 'imgs\plugin\category-icon@2x.png') -Size 56 -Transparent

# One action folder per manifest action, each with its own glyph.
$actions = [ordered]@{
    entry   = 'crystal'
    slot    = 'cell'
    prev    = 'left'
    next    = 'right'
    kind    = 'swap'
    vitals  = 'heart'
    job     = 'shield'
    duty    = 'flag'
    venture = 'hourglass'
    recast  = 'ring'
}

foreach ($name in $actions.Keys) {
    $glyph = $actions[$name]
    $dir = Join-Path $PluginRoot "imgs\actions\$name"

    # A slot spends its life holding someone else's icon, so its own tile is black: an
    # empty cell should read as empty rather than as another panel.
    $tile = if ($name -eq 'slot') { $black } else { $background }

    Save-Icon -Path (Join-Path $dir 'icon.png') -Size 20 -Glyph $glyph -Transparent
    Save-Icon -Path (Join-Path $dir 'icon@2x.png') -Size 40 -Glyph $glyph -Transparent

    Save-Icon -Path (Join-Path $dir 'key.png') -Size 72 -Glyph $glyph -Tile $tile
    Save-Icon -Path (Join-Path $dir 'key@2x.png') -Size 144 -Glyph $glyph -Tile $tile
}

Write-Host "done" -ForegroundColor Green
