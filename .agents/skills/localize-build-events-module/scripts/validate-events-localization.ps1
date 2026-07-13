[CmdletBinding()]
param(
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
} else {
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
}

function Read-ResxMap {
    param([Parameter(Mandatory = $true)][string]$Path)

    [xml]$document = Get-Content -Raw -LiteralPath $Path
    $map = @{}
    $duplicates = [System.Collections.Generic.List[string]]::new()

    foreach ($node in @($document.root.data)) {
        $key = [string]$node.name
        if ($map.ContainsKey($key)) {
            $duplicates.Add($key)
        } else {
            $map[$key] = [string]$node.value
        }
    }

    [pscustomobject]@{
        Map        = $map
        Duplicates = @($duplicates)
    }
}

$eventsPath = Join-Path $RepoRoot 'Events Module\ref\events.json'
$neutralPath = Join-Path $RepoRoot 'Events Module\Properties\Resources.resx'
$localizedPath = Join-Path $RepoRoot 'Events Module\Properties\Resources.zh.resx'

foreach ($path in @($eventsPath, $neutralPath, $localizedPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$events = @(Get-Content -Raw -LiteralPath $eventsPath | ConvertFrom-Json)
$neutral = Read-ResxMap -Path $neutralPath
$localized = Read-ResxMap -Path $localizedPath

$eventStrings = @(
    $events |
        ForEach-Object { [string]$_.category; [string]$_.name } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)

$requiredKeys = @(
    @($neutral.Map.Keys) + $eventStrings |
        Sort-Object -Unique
)

$missing = @($requiredKeys | Where-Object { -not $localized.Map.ContainsKey($_) })
$englishEventValues = @(
    $eventStrings |
        Where-Object { $localized.Map.ContainsKey($_) -and $localized.Map[$_] -match '[A-Za-z]' }
)

$placeholderMismatches = [System.Collections.Generic.List[string]]::new()
foreach ($key in @($neutral.Map.Keys)) {
    if (-not $localized.Map.ContainsKey($key)) {
        continue
    }

    $sourceTokens = @(
        [regex]::Matches($neutral.Map[$key], '\{\d+(?::[^}]*)?\}') |
            ForEach-Object { $_.Value } |
            Sort-Object
    )
    $targetTokens = @(
        [regex]::Matches($localized.Map[$key], '\{\d+(?::[^}]*)?\}') |
            ForEach-Object { $_.Value } |
            Sort-Object
    )

    if (@(Compare-Object -ReferenceObject $sourceTokens -DifferenceObject $targetTokens).Count -gt 0) {
        $placeholderMismatches.Add($key)
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
if ($localized.Duplicates.Count -gt 0) {
    $failures.Add("Duplicate resource keys: $($localized.Duplicates -join ', ')")
}
if ($missing.Count -gt 0) {
    $failures.Add("Missing Traditional Chinese resources: $($missing -join ', ')")
}
if ($englishEventValues.Count -gt 0) {
    $failures.Add("Event translations still contain English text: $($englishEventValues -join ', ')")
}
if ($placeholderMismatches.Count -gt 0) {
    $failures.Add("Placeholder mismatches: $($placeholderMismatches -join ', ')")
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

$extraKeys = @($localized.Map.Keys | Where-Object { $_ -notin $requiredKeys })

Write-Host "Validated $($events.Count) event records."
Write-Host "Validated $($eventStrings.Count) unique event names/categories."
Write-Host "Validated $($requiredKeys.Count) required Traditional Chinese resources."
if ($extraKeys.Count -gt 0) {
    Write-Warning "Unused localized resource keys: $($extraKeys -join ', ')"
}

[pscustomobject]@{
    RepoRoot         = $RepoRoot
    EventRecordCount = $events.Count
    EventStringCount = $eventStrings.Count
    RequiredKeyCount = $requiredKeys.Count
    LocalizedCount   = $localized.Map.Count
    ExtraKeyCount    = $extraKeys.Count
}
