[CmdletBinding()]
param(
    [string]$RepoRoot,
    [switch]$Live,
    [string]$WaitForCommit,
    [ValidateRange(10, 600)]
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path $PSScriptRoot "..\..\..\.."
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$DocsRoot = Join-Path $RepoRoot "docs"
$IndexPath = Join-Path $DocsRoot "index.html"
$StylesPath = Join-Path $DocsRoot "styles.css"
$ScriptPath = Join-Path $DocsRoot "script.js"
$SiteUrl = "https://gw.jakeuj.com/"
$Repository = "jakeuj/Community-Module-Pack"

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Condition -Condition $Content.Contains($Value) -Message "$Context is missing required value: $Value"
}

function Get-HttpStatusCode {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)]
        [string]$Uri
    )

    $response = $Client.GetAsync($Uri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    try {
        return [int]$response.StatusCode
    }
    finally {
        $response.Dispose()
    }
}

$requiredFiles = @(
    "index.html",
    "styles.css",
    "script.js",
    "CNAME",
    "assets/favicon.png",
    "assets/jakeuj-gw2-tools-og.jpg",
    "assets/hero-tyria-map-960.webp"
)

foreach ($relativePath in $requiredFiles) {
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $DocsRoot $relativePath) -PathType Leaf) `
        -Message "Missing GitHub Pages file: docs/$relativePath"
}

$html = Get-Content -LiteralPath $IndexPath -Raw -Encoding UTF8
$styles = Get-Content -LiteralPath $StylesPath -Raw -Encoding UTF8
$script = Get-Content -LiteralPath $ScriptPath -Raw -Encoding UTF8
$cname = (Get-Content -LiteralPath (Join-Path $DocsRoot "CNAME") -Raw -Encoding UTF8).Trim()
Assert-Condition -Condition ($cname -eq "gw.jakeuj.com") -Message "docs/CNAME must contain only gw.jakeuj.com."

$requiredHtmlValues = @(
    '<html lang="zh-Hant-TW">',
    'jakeuj GW2 Tools',
    'Blish HUD Module',
    'Nexus Addon',
    'ArcDPS Plugin',
    'https://gw2-value.jakeuj.com/',
    'https://gw2.jakeuj.com/',
    'https://github.com/jakeuj/Community-Module-Pack/releases/latest/download/Events.Module.bhm',
    'type="application/ld+json"',
    'property="og:image"',
    'name="author"',
    'data-fallback-version='
)

foreach ($value in $requiredHtmlValues) {
    Assert-Contains -Content $html -Value $value -Context "docs/index.html"
}

Assert-Contains -Content $styles -Value "prefers-reduced-motion: reduce" -Context "docs/styles.css"
Assert-Contains -Content $script -Value "window.setTimeout(() => controller.abort(), 3000)" -Context "docs/script.js"
Assert-Contains -Content $script -Value "Events.Module.bhm" -Context "docs/script.js"
Assert-Contains -Content $script -Value "sha256:[0-9a-f]{64}" -Context "docs/script.js"

$jsonLdMatch = [regex]::Match(
    $html,
    '<script\s+type="application/ld\+json">\s*(?<json>.*?)\s*</script>',
    [System.Text.RegularExpressions.RegexOptions]::Singleline
)
Assert-Condition -Condition $jsonLdMatch.Success -Message "docs/index.html has no readable JSON-LD block."
$jsonLd = $jsonLdMatch.Groups["json"].Value | ConvertFrom-Json
$jsonLdTypes = @($jsonLd."@graph" | ForEach-Object { $_."@type" })
Assert-Condition -Condition ($jsonLdTypes -contains "SoftwareApplication") -Message "JSON-LD is missing SoftwareApplication."
Assert-Condition -Condition ($jsonLdTypes -contains "SoftwareSourceCode") -Message "JSON-LD is missing SoftwareSourceCode."

$imageTags = [regex]::Matches($html, '<img\b[^>]*>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
foreach ($match in $imageTags) {
    Assert-Condition -Condition ([regex]::IsMatch($match.Value, '\balt="[^"]*"', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) `
        -Message "Every image must include alt text: $($match.Value)"
}

$localReferenceMatches = [regex]::Matches(
    "$html`n$styles",
    '\./[A-Za-z0-9._/-]+',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
)
$localReferences = @($localReferenceMatches | ForEach-Object { $_.Value } | Sort-Object -Unique)
foreach ($reference in $localReferences) {
    $relativePath = $reference.Substring(2).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    Assert-Condition -Condition (Test-Path -LiteralPath (Join-Path $DocsRoot $relativePath) -PathType Leaf) `
        -Message "Missing local site reference: $reference"
}

Add-Type -AssemblyName System.Drawing
$ogImage = [System.Drawing.Image]::FromFile((Join-Path $DocsRoot "assets\jakeuj-gw2-tools-og.jpg"))
try {
    Assert-Condition -Condition ($ogImage.Width -eq 1200 -and $ogImage.Height -eq 630) `
        -Message "Open Graph image must be 1200x630; found $($ogImage.Width)x$($ogImage.Height)."
}
finally {
    $ogImage.Dispose()
}

$node = Get-Command node -ErrorAction SilentlyContinue
Assert-Condition -Condition ($null -ne $node) -Message "Node.js is required to validate docs/script.js."
& $node.Source --check $ScriptPath
Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "node --check failed for docs/script.js."

$git = Get-Command git -ErrorAction SilentlyContinue
Assert-Condition -Condition ($null -ne $git) -Message "Git is required to validate site whitespace."
& $git.Source -C $RepoRoot diff --check -- docs
Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "git diff --check failed for docs."

$pagesCommit = $null
$pagesSource = $null
$liveStatuses = [ordered]@{}

if (-not [string]::IsNullOrWhiteSpace($WaitForCommit)) {
    $Live = $true
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    Assert-Condition -Condition ($null -ne $gh) -Message "GitHub CLI is required for -WaitForCommit."

    $pagesJson = & $gh.Source api "repos/$Repository/pages"
    Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "Could not read the GitHub Pages configuration."
    $pages = $pagesJson | ConvertFrom-Json
    Assert-Condition -Condition ($pages.source.branch -eq "master" -and $pages.source.path -eq "/docs") `
        -Message "GitHub Pages must deploy from master:/docs."
    Assert-Condition -Condition ($pages.html_url -eq $SiteUrl) -Message "GitHub Pages URL does not match $SiteUrl."
    Assert-Condition -Condition ([bool]$pages.https_enforced) -Message "GitHub Pages must enforce HTTPS."
    $pagesSource = "$($pages.source.branch):$($pages.source.path)"

    $resolvedCommit = (& $git.Source -C $RepoRoot rev-parse "$WaitForCommit^{commit}").Trim()
    Assert-Condition -Condition ($LASTEXITCODE -eq 0 -and $resolvedCommit -match '^[0-9a-f]{40}$') `
        -Message "Could not resolve commit: $WaitForCommit"

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $buildJson = & $gh.Source api "repos/$Repository/pages/builds/latest"
        Assert-Condition -Condition ($LASTEXITCODE -eq 0) -Message "Could not read the latest GitHub Pages build."
        $build = $buildJson | ConvertFrom-Json
        Write-Host "Pages status=$($build.status) commit=$($build.commit)"

        if ($build.status -eq "errored") {
            throw "GitHub Pages build failed: $($build.error.message)"
        }

        if ($build.commit -eq $resolvedCommit -and $build.status -eq "built") {
            $pagesCommit = $build.commit
            break
        }

        Start-Sleep -Seconds 5
    } while ([DateTime]::UtcNow -lt $deadline)

    Assert-Condition -Condition ($pagesCommit -eq $resolvedCommit) `
        -Message "Timed out waiting for GitHub Pages to build commit $resolvedCommit."
}

if ($Live) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $true
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd("jakeuj-events-pages-validator/1.0")

    try {
        $cacheBust = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $liveHtml = $client.GetStringAsync("${SiteUrl}?v=$cacheBust").GetAwaiter().GetResult()
        foreach ($value in @("jakeuj GW2 Tools", "Blish HUD Module", "Nexus Addon", "ArcDPS Plugin")) {
            Assert-Contains -Content $liveHtml -Value $value -Context $SiteUrl
        }

        $liveTargets = [ordered]@{
            Site = $SiteUrl
            HeroAsset = "${SiteUrl}assets/hero-tyria-map-960.webp?v=$cacheBust"
            UpgradeValue = "https://gw2-value.jakeuj.com/"
            ArcDpsZhTw = "https://gw2.jakeuj.com/"
            LatestRelease = "https://github.com/jakeuj/Community-Module-Pack/releases/latest"
            Download = "https://github.com/jakeuj/Community-Module-Pack/releases/latest/download/Events.Module.bhm"
        }

        foreach ($target in $liveTargets.GetEnumerator()) {
            $statusCode = Get-HttpStatusCode -Client $client -Uri $target.Value
            Assert-Condition -Condition ($statusCode -ge 200 -and $statusCode -lt 400) `
                -Message "$($target.Key) returned HTTP ${statusCode}: $($target.Value)"
            $liveStatuses[$target.Key] = $statusCode
        }
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

[pscustomobject]@{
    RepoRoot = $RepoRoot
    ImageCount = $imageTags.Count
    LocalReferenceCount = $localReferences.Count
    JsonLdTypes = $jsonLdTypes -join ", "
    NodeSyntax = "Passed"
    Live = [bool]$Live
    PagesSource = $pagesSource
    PagesCommit = $pagesCommit
    LiveStatuses = if ($liveStatuses.Count -gt 0) { ($liveStatuses.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ", " } else { $null }
}
