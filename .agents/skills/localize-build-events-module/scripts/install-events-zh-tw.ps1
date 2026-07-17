[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$SourceBhm,
    [string]$DestinationBhm,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

if ([string]::IsNullOrWhiteSpace($SourceBhm)) {
    $SourceBhm = Join-Path $RepoRoot "artifacts\Events-and-Metas-Observer-zh-TW\Events Module.bhm"
}

if ([string]::IsNullOrWhiteSpace($DestinationBhm)) {
    $documentsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    if ([string]::IsNullOrWhiteSpace($documentsPath)) {
        throw "Windows did not return a Documents folder path. Pass -DestinationBhm explicitly."
    }

    $DestinationBhm = Join-Path $documentsPath "Guild Wars 2\addons\blishhud\modules\Events Module.bhm"
}

$SourceBhm = [IO.Path]::GetFullPath($SourceBhm)
$DestinationBhm = [IO.Path]::GetFullPath($DestinationBhm)

if (-not (Test-Path -LiteralPath $SourceBhm -PathType Leaf)) {
    throw "Built BHM was not found: $SourceBhm"
}

if ([StringComparer]::OrdinalIgnoreCase.Equals($SourceBhm, $DestinationBhm)) {
    throw "Source and destination BHM paths must be different."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-BhmInspection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
        $entrySet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($entryName in $entryNames) {
            [void]$entrySet.Add($entryName)
        }

        $requiredEntries = @("manifest.json", "Events Module.dll", "ref/events.json", "ref/event-rewards.json")
        $missingEntries = @($requiredEntries | Where-Object { -not $entrySet.Contains($_) })
        if ($missingEntries.Count -gt 0) {
            throw "BHM is missing required entries: $($missingEntries -join ', ')"
        }

        $eventIconEntries = @(
            $entryNames |
                Where-Object { $_ -match '^ref/textures/events/[^/]+\.png$' } |
                Sort-Object -Unique
        )

        [pscustomobject]@{
            EntryCount       = $entryNames.Count
            EventIconCount   = $eventIconEntries.Count
            EventIconEntries = $eventIconEntries
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$sourceInspection = Get-BhmInspection -Path $SourceBhm
$sourceIconDirectory = Join-Path $RepoRoot "Events Module\ref\textures\events"
if (Test-Path -LiteralPath $sourceIconDirectory -PathType Container) {
    $expectedIconEntries = @(
        Get-ChildItem -LiteralPath $sourceIconDirectory -Filter "*.png" -File |
            ForEach-Object { "ref/textures/events/$($_.Name)" } |
            Sort-Object -Unique
    )
    $packagedIconSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($entryName in $sourceInspection.EventIconEntries) {
        [void]$packagedIconSet.Add($entryName)
    }

    $missingPackagedIcons = @($expectedIconEntries | Where-Object { -not $packagedIconSet.Contains($_) })
    if ($missingPackagedIcons.Count -gt 0) {
        throw "Built BHM is missing source event icons: $($missingPackagedIcons -join ', ')"
    }
}

$sourceHash = Get-Sha256 -Path $SourceBhm
$sourceFile = Get-Item -LiteralPath $SourceBhm
$destinationExists = Test-Path -LiteralPath $DestinationBhm -PathType Leaf
$destinationHash = $null
$destinationFile = $null
$destinationInspection = $null
$destinationInspectionError = $null

if ($destinationExists) {
    $destinationFile = Get-Item -LiteralPath $DestinationBhm
    $destinationHash = Get-Sha256 -Path $DestinationBhm
    try {
        $destinationInspection = Get-BhmInspection -Path $DestinationBhm
    }
    catch {
        $destinationInspectionError = $_.Exception.Message
    }
}

$blishHudRunning = @(Get-Process -Name "Blish HUD" -ErrorAction SilentlyContinue).Count -gt 0
$matches = $destinationExists -and [StringComparer]::OrdinalIgnoreCase.Equals($sourceHash, $destinationHash)

$comparison = [pscustomobject]@{
    SourceBhm                    = $SourceBhm
    SourceLength                 = $sourceFile.Length
    SourceLastWriteTimeUtc       = $sourceFile.LastWriteTimeUtc
    SourceSha256                 = $sourceHash
    SourceIconCount              = $sourceInspection.EventIconCount
    DestinationBhm               = $DestinationBhm
    DestinationExists            = $destinationExists
    DestinationLength            = if ($null -eq $destinationFile) { $null } else { $destinationFile.Length }
    DestinationLastWriteTimeUtc  = if ($null -eq $destinationFile) { $null } else { $destinationFile.LastWriteTimeUtc }
    DestinationSha256            = $destinationHash
    DestinationIconCount         = if ($null -eq $destinationInspection) { $null } else { $destinationInspection.EventIconCount }
    DestinationInspectionError   = $destinationInspectionError
    Matches                      = $matches
    BlishHudRunning              = $blishHudRunning
    RestartRequired              = $blishHudRunning -or -not $matches
}

if ($CheckOnly) {
    $comparison
    return
}

if ($matches) {
    $comparison | Add-Member -NotePropertyName Installed -NotePropertyValue $false
    $comparison | Add-Member -NotePropertyName InstallStatus -NotePropertyValue "AlreadyCurrent"
    $comparison
    return
}

if ($blishHudRunning) {
    throw "Blish HUD is running. Fully exit it before replacing the installed Events Module BHM. No files were changed."
}

$destinationDirectory = Split-Path -Parent $DestinationBhm
if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $destinationDirectory -Force)
}

$backupPath = $null
if ($destinationExists) {
    $backupPath = "$DestinationBhm.$(Get-Date -Format 'yyyyMMdd-HHmmss').bak"
    if (Test-Path -LiteralPath $backupPath) {
        $backupPath = "$DestinationBhm.$(Get-Date -Format 'yyyyMMdd-HHmmss')-$([Guid]::NewGuid().ToString('N')).bak"
    }
    Copy-Item -LiteralPath $DestinationBhm -Destination $backupPath
}

$temporaryPath = Join-Path $destinationDirectory (".events-module-{0}.bhm" -f [Guid]::NewGuid().ToString("N"))
try {
    Copy-Item -LiteralPath $SourceBhm -Destination $temporaryPath
    $temporaryHash = Get-Sha256 -Path $temporaryPath
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($sourceHash, $temporaryHash)) {
        throw "Temporary BHM hash did not match the build artifact."
    }

    if (Test-Path -LiteralPath $DestinationBhm -PathType Leaf) {
        [IO.File]::Replace($temporaryPath, $DestinationBhm, $null, $true)
    }
    else {
        [IO.File]::Move($temporaryPath, $DestinationBhm)
    }

    $installedHash = Get-Sha256 -Path $DestinationBhm
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($sourceHash, $installedHash)) {
        throw "Installed BHM hash did not match the build artifact. Backup: $backupPath"
    }

    $installedInspection = Get-BhmInspection -Path $DestinationBhm
    [pscustomobject]@{
        SourceBhm          = $SourceBhm
        DestinationBhm     = $DestinationBhm
        Installed          = $true
        InstallStatus      = "Installed"
        BackupPath         = $backupPath
        Sha256             = $installedHash
        EventIconCount     = $installedInspection.EventIconCount
        BlishHudRunning    = $false
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}
