[CmdletBinding()]
param(
    [string]$RepoRoot,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutDir = 'artifacts\Events-and-Metas-Observer-zh-TW',
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
} else {
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
}

function Find-MSBuild {
    $command = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $found = @(
            & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe'
        ) | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($found)) {
            return $found
        }
    }

    throw 'MSBuild was not found. Install Visual Studio Build Tools with the MSBuild component.'
}

function Ensure-Net472ReferencePack {
    param([Parameter(Mandatory = $true)][string]$Root)

    $installedPack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\mscorlib.dll'
    if (Test-Path -LiteralPath $installedPack) {
        return $null
    }

    $version = '1.0.3'
    $packageDir = Join-Path $Root "packages\Microsoft.NETFramework.ReferenceAssemblies.net472.$version"
    $referenceAssembly = Join-Path $packageDir 'build\.NETFramework\v4.7.2\mscorlib.dll'

    if (-not (Test-Path -LiteralPath $referenceAssembly)) {
        New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
        $archive = Join-Path $packageDir 'reference-pack.zip'
        $url = "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net472/$version/microsoft.netframework.referenceassemblies.net472.$version.nupkg"

        Write-Host "Downloading .NET Framework 4.7.2 reference assemblies $version..."
        Invoke-WebRequest -Uri $url -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $packageDir -Force
        Remove-Item -LiteralPath $archive
    }

    if (-not (Test-Path -LiteralPath $referenceAssembly)) {
        throw "Failed to prepare .NET Framework 4.7.2 reference assemblies: $referenceAssembly"
    }

    return (Join-Path $packageDir 'build\')
}

$validator = Join-Path $PSScriptRoot 'validate-events-localization.ps1'
$validation = & $validator -RepoRoot $RepoRoot
$iconValidator = Join-Path $RepoRoot 'Events Module\Tests\ValidateEventIcons.ps1'
$iconSourceDirectory = Join-Path $RepoRoot 'Events Module\ref\textures\events'

foreach ($path in @($iconValidator, $iconSourceDirectory)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required event icon validation path not found: $path"
    }
}

& $iconValidator
$iconFiles = @(Get-ChildItem -LiteralPath $iconSourceDirectory -Filter '*.png' -File | Sort-Object Name)
if ($iconFiles.Count -eq 0) {
    throw "No event icons were found in $iconSourceDirectory."
}

$msbuild = Find-MSBuild
$solution = Join-Path $RepoRoot 'Community Module Pack.sln'
$project = Join-Path $RepoRoot 'Events Module\Events Module.csproj'

if (-not $SkipRestore) {
    Write-Host 'Restoring NuGet packages...'
    & $msbuild $solution /t:Restore /p:RestorePackagesConfig=true /m /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Package restore failed with exit code $LASTEXITCODE."
    }
}

$frameworkRoot = Ensure-Net472ReferencePack -Root $RepoRoot

if ([System.IO.Path]::IsPathRooted($OutDir)) {
    $outputPath = [System.IO.Path]::GetFullPath($OutDir)
} else {
    $outputPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutDir))
}
if (-not $outputPath.EndsWith([string][System.IO.Path]::DirectorySeparatorChar)) {
    $outputPath += [System.IO.Path]::DirectorySeparatorChar
}

$arguments = @(
    $project,
    '/t:Rebuild',
    "/p:Configuration=$Configuration",
    '/p:Platform=AnyCPU',
    '/p:ChineseBuild=true',
    "/p:OutDir=$outputPath",
    '/verbosity:minimal'
)
if ($null -ne $frameworkRoot) {
    $arguments += "/p:TargetFrameworkRootPath=$frameworkRoot"
}

Write-Host "Building standalone Traditional Chinese module into $outputPath"
& $msbuild @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$dll = Join-Path $outputPath 'Events Module.dll'
$bhm = Join-Path $outputPath 'Events Module.bhm'
foreach ($path in @($dll, $bhm)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected build output was not created: $path"
    }
}

$assembly = [System.Reflection.Assembly]::LoadFile($dll)
$resourceStream = $assembly.GetManifestResourceStream('Events_Module.Properties.Resources.resources')
if ($null -eq $resourceStream) {
    throw 'The DLL does not contain the neutral Traditional Chinese resource stream.'
}

$reader = [System.Resources.ResourceReader]::new($resourceStream)
$resourceCount = 0
$enumerator = $reader.GetEnumerator()
while ($enumerator.MoveNext()) {
    $resourceCount++
}
$reader.Close()
if ($resourceCount -lt $validation.RequiredKeyCount) {
    throw "The DLL contains $resourceCount resources; expected at least $($validation.RequiredKeyCount)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($bhm)
try {
    $requiredEntries = @('Events Module.dll', 'manifest.json', 'ref/events.json') + @(
        $iconFiles | ForEach-Object { "ref/textures/events/$($_.Name)" }
    )
    foreach ($entryName in $requiredEntries) {
        if ($null -eq $archive.GetEntry($entryName)) {
            throw "BHM package is missing $entryName."
        }
    }
} finally {
    $archive.Dispose()
}

$result = [pscustomobject]@{
    DllPath       = $dll
    BhmPath       = $bhm
    ResourceCount = $resourceCount
    IconCount     = $iconFiles.Count
    DllSha256     = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    BhmSha256     = (Get-FileHash -LiteralPath $bhm -Algorithm SHA256).Hash
}

Write-Host "BHM event icons: $($result.IconCount)"
Write-Host "DLL SHA-256: $($result.DllSha256)"
Write-Host "BHM SHA-256: $($result.BhmSha256)"
$result
