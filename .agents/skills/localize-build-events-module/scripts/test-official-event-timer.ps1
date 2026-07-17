[CmdletBinding()]
param(
    [string]$RepoRoot,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Live,
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

$msbuild = Find-MSBuild
$solution = Join-Path $RepoRoot 'Community Module Pack.sln'
$project = Join-Path $RepoRoot 'Events Module\Tests\OfficialEventTimerParser.Tests.csproj'
$newtonsoft = Join-Path $RepoRoot 'packages\Newtonsoft.Json.13.0.1\lib\net45\Newtonsoft.Json.dll'

foreach ($path in @($solution, $project)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

if (-not $SkipRestore -and -not (Test-Path -LiteralPath $newtonsoft)) {
    Write-Host 'Restoring NuGet packages...'
    & $msbuild $solution /t:Restore /p:RestorePackagesConfig=true /m /verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Package restore failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $newtonsoft)) {
    throw "Newtonsoft.Json reference not found: $newtonsoft"
}

$frameworkRoot = Ensure-Net472ReferencePack -Root $RepoRoot
$arguments = @(
    $project,
    '/t:Rebuild',
    "/p:Configuration=$Configuration",
    '/p:Platform=AnyCPU',
    '/m',
    '/verbosity:minimal'
)
if ($null -ne $frameworkRoot) {
    $arguments += "/p:TargetFrameworkRootPath=$frameworkRoot"
}

& $msbuild @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Official event timer parser test build failed with exit code $LASTEXITCODE."
}

$testExecutable = Join-Path $RepoRoot "Events Module\Tests\bin\$Configuration\OfficialEventTimerParser.Tests.exe"
if (-not (Test-Path -LiteralPath $testExecutable)) {
    throw "Parser test executable was not created: $testExecutable"
}

$testArguments = @()
if ($Live) {
    $testArguments += '--live'
}

& $testExecutable @testArguments
if ($LASTEXITCODE -ne 0) {
    throw "Official event timer parser tests failed with exit code $LASTEXITCODE."
}

[pscustomobject]@{
    RepoRoot      = $RepoRoot
    Configuration = $Configuration
    LiveAudit     = [bool]$Live
    TestExecutable = $testExecutable
}
