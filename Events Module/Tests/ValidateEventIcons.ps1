$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$iconDirectory = Join-Path $PSScriptRoot "..\ref\textures\events"
$expectedIcons = @(
    "day.png",
    "dusk.png",
    "night.png",
    "dawn.png",
    "tournament-balthazar.png",
    "tournament-grenth.png",
    "tournament-melandru.png",
    "tournament-lyssa.png",
    "invasion-awakened.png",
    "invasion-scarlet.png",
    "shackles-of-the-ancients.png"
)

foreach ($filename in $expectedIcons) {
    $path = Join-Path $iconDirectory $filename
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing event icon: $path"
    }

    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        if ($bitmap.Width -ne 64 -or $bitmap.Height -ne 64) {
            throw "$filename must be 64x64, found $($bitmap.Width)x$($bitmap.Height)."
        }

        if (-not [System.Drawing.Image]::IsAlphaPixelFormat($bitmap.PixelFormat)) {
            throw "$filename must use an alpha-capable PNG pixel format."
        }

        $cornerAlpha = @(
            $bitmap.GetPixel(0, 0).A,
            $bitmap.GetPixel(63, 0).A,
            $bitmap.GetPixel(0, 63).A,
            $bitmap.GetPixel(63, 63).A
        )
        if (@($cornerAlpha | Where-Object { $_ -ne 0 }).Count -gt 0) {
            throw "$filename must have fully transparent corners."
        }

        $visiblePixels = 0
        $keyColorPixels = 0
        for ($y = 0; $y -lt 64; $y++) {
            for ($x = 0; $x -lt 64; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.A -le 16) { continue }

                $visiblePixels++
                $looksGreenKey = $pixel.G -gt 220 -and $pixel.R -lt 45 -and $pixel.B -lt 45
                $looksMagentaKey = $pixel.R -gt 220 -and $pixel.B -gt 220 -and $pixel.G -lt 45
                if ($looksGreenKey -or $looksMagentaKey) { $keyColorPixels++ }
            }
        }

        $coverage = $visiblePixels / 4096.0
        if ($coverage -lt 0.45 -or $coverage -gt 0.80) {
            throw "$filename has implausible visible coverage: $([Math]::Round($coverage * 100, 1))%."
        }

        if ($keyColorPixels -gt 0) {
            throw "$filename contains $keyColorPixels visible chroma-key pixels."
        }
    } finally {
        $bitmap.Dispose()
    }
}

$unexpectedIcons = @(
    Get-ChildItem -LiteralPath $iconDirectory -Filter "*.png" -File |
        Where-Object { $_.Name -notin $expectedIcons }
)
if ($unexpectedIcons.Count -gt 0) {
    throw "Unexpected event icons: $($unexpectedIcons.Name -join ', ')"
}

Write-Host "Validated $($expectedIcons.Count) transparent 64x64 event icons."
