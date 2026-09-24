# publish-release.ps1 - Builds the release package on a developer machine with Inventor 2027 and
#   attaches it to the draft GitHub Release the release workflow created, then publishes the draft.
#
# Purpose: Close the gap that shipped v0.6.0 broken. Every add-in project defines INVENTOR_INTEROP only
#   when Autodesk.Inventor.Interop.dll exists at build time, and that DLL ships only with an Inventor
#   install. A GitHub-hosted runner has no Inventor, so any add-in it builds compiles its entry point
#   out and loads in Inventor as Unloaded with no ribbon command. The workflow therefore verifies the
#   tag, runs the gate, and creates a DRAFT release with notes and no assets; this script is the one
#   place assets are built and attached (ADR-0005, 2026-09-24 amendment).
# Inputs: -Version (the release version without the leading v), -Repo (owner/name), -DistRoot
#   (optional; passed to build-release.ps1), -SkipBuild (package the existing Release output),
#   -AllowUntagged (package a HEAD that is not the tag; printed loudly), -DryRun or -WhatIf (stop
#   after the interop guard and print what would be uploaded, without calling GitHub).
# Outputs: the three release assets uploaded to v<Version>, the release published (non-draft), and
#   the asset list GitHub reports afterwards.
# Dependencies: Windows PowerShell 5.1 or later, git, gh (authenticated for -Repo), the .NET SDK
#   pinned by global.json, Inventor 2027 installed, build-release.ps1 beside this script.
# Assumptions: The draft release v<Version> already exists because the tag push ran
#   .github/workflows/release.yml. The working tree is clean and at the tag, so the uploaded binaries
#   are built from exactly the commit the tag names.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Repo = "srinator22/Smart-Manufacturing-Macro",

    [string]$DistRoot,

    [switch]$SkipBuild,

    [switch]$AllowUntagged,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

# -WhatIf is honored as a dry run explicitly. Its preference variable is reset so the child
# build-release.ps1 really builds and stages; otherwise the guard would run against nothing.
$isDryRun = $DryRun.IsPresent -or [bool]$WhatIfPreference
$WhatIfPreference = $false

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$tag = "v$Version"
$buildScript = Join-Path $PSScriptRoot "build-release.ps1"
if ([string]::IsNullOrWhiteSpace($DistRoot)) {
    $DistRoot = Join-Path $repoRoot "dist"
}
$distRoot = [System.IO.Path]::GetFullPath($DistRoot)
$zipPath = Join-Path $distRoot "WmpInventorTools-$Version.zip"
$sumsPath = Join-Path $distRoot "SHA256SUMS.txt"
$installerPath = Join-Path (Join-Path $distRoot "WmpInventorTools-$Version") "Install-WmpInventorTools.ps1"
# Same default as InventorInteropPath in every add-in csproj; if this file is absent the build
# compiles the add-in entry points out.
$interopPath = Join-Path $env:ProgramFiles "Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.dll"

function Invoke-Git {
    param([string[]]$Arguments)

    # Windows PowerShell 5.1 turns redirected native stderr into a terminating error under Stop;
    # the exit code is the failure signal here, so stderr is only collected.
    $ErrorActionPreference = "Continue"
    $output = & git -C $repoRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code ${LASTEXITCODE}: $output"
    }
    return (($output | Out-String).TrimEnd())
}

function Invoke-Gh {
    param([string[]]$Arguments)

    # Windows PowerShell 5.1 turns redirected native stderr into a terminating error under Stop;
    # the exit code is the failure signal here, so stderr is only collected.
    $ErrorActionPreference = "Continue"
    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed with exit code ${LASTEXITCODE}: $output"
    }
    return (($output | Out-String).TrimEnd())
}

if ($isDryRun) {
    Write-Output "publish-release: DRY RUN - builds and runs the interop guard, then stops before any GitHub call."
}

# In a dry run the preconditions are collected rather than thrown, so one run reports every reason
# publishing would be refused and still exercises the build and the guard. The run still exits 1.
$refusals = @()

$head = Invoke-Git @("rev-parse", "HEAD")
$tagCommit = $null
$ErrorActionPreference = "Continue"
& git -C $repoRoot rev-parse --verify --quiet "$tag^{commit}" > $null 2>&1
$tagProbeExit = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($tagProbeExit -eq 0) {
    $tagCommit = Invoke-Git @("rev-parse", "$tag^{commit}")
}
if ($null -eq $tagCommit) {
    $reason = "the tag $tag does not exist locally; fetch tags (git fetch --tags) or push the tag first."
    if ($AllowUntagged) {
        Write-Warning "publish-release: -AllowUntagged - $reason Packaging HEAD $head anyway."
    }
    else {
        $refusals += $reason
    }
}
elseif ($head -ne $tagCommit) {
    $reason = "HEAD $head is not the commit $tagCommit that $tag names; check out the tag so the binaries match the release."
    if ($AllowUntagged) {
        Write-Warning "publish-release: -AllowUntagged - $reason Packaging HEAD anyway; the uploaded binaries will NOT be built from $tag."
    }
    else {
        $refusals += $reason
    }
}

$status = Invoke-Git @("status", "--porcelain")
if (-not [string]::IsNullOrWhiteSpace($status)) {
    $refusals += "the working tree has uncommitted or untracked changes, so the package would not be the committed source:`n$status"
}

if ($refusals.Count -gt 0 -and -not $isDryRun) {
    throw ("publish-release: refusing to publish $tag`: " + ($refusals -join "`n"))
}

if (-not (Test-Path -LiteralPath $interopPath -PathType Leaf)) {
    throw "publish-release: Autodesk.Inventor.Interop.dll is not at '$interopPath'. Run this on a machine with Inventor 2027 installed; without it every add-in compiles its entry point out."
}
Write-Output "publish-release: Inventor interop found at $interopPath"

# build-release.ps1 verifies -Version against Directory.Build.props and refuses any add-in DLL that
# lacks the interop reference or StandardAddInServer before it writes a package.
$buildArguments = @{ Version = $Version; DistRoot = $distRoot }
if ($SkipBuild) { $buildArguments["SkipBuild"] = $true }
& $buildScript @buildArguments
foreach ($asset in @($zipPath, $sumsPath, $installerPath)) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "publish-release: build-release.ps1 finished but '$asset' is missing."
    }
}

$assets = @($zipPath, $sumsPath, $installerPath)
if ($isDryRun) {
    Write-Output "publish-release: DRY RUN - the interop guard passed. Would upload to $Repo release $tag`:"
    foreach ($asset in $assets) {
        $item = Get-Item -LiteralPath $asset
        Write-Output ("  {0} ({1} bytes)" -f $item.FullName, $item.Length)
    }
    Write-Output "publish-release: DRY RUN - would run: gh release view $tag --repo $Repo"
    Write-Output "publish-release: DRY RUN - would run: gh release upload $tag <the three files above> --repo $Repo --clobber"
    Write-Output "publish-release: DRY RUN - would run: gh release edit $tag --repo $Repo --draft=false"
    if ($refusals.Count -gt 0) {
        Write-Output "publish-release: DRY RUN - a real run would be REFUSED:"
        foreach ($reason in $refusals) { Write-Output "  - $reason" }
        exit 1
    }
    Write-Output "publish-release: DRY RUN - OK; a real run would publish."
    exit 0
}

$releaseJson = Invoke-Gh @("release", "view", $tag, "--repo", $Repo, "--json", "tagName,isDraft")
$release = $releaseJson | ConvertFrom-Json
if (-not $release.isDraft) {
    Write-Warning "publish-release: $tag is already published; its assets will be replaced (--clobber)."
}

Invoke-Gh (@("release", "upload", $tag) + $assets + @("--repo", $Repo, "--clobber")) | Out-Null
Write-Output "publish-release: uploaded $($assets.Count) assets to $tag"
Invoke-Gh @("release", "edit", $tag, "--repo", $Repo, "--draft=false") | Out-Null
Write-Output "publish-release: $tag is published"

$published = Invoke-Gh @("release", "view", $tag, "--repo", $Repo, "--json", "isDraft,assets") | ConvertFrom-Json
$names = @($published.assets | ForEach-Object { $_.name })
foreach ($asset in $assets) {
    $name = [System.IO.Path]::GetFileName($asset)
    if ($names -notcontains $name) {
        throw "publish-release: GitHub does not list '$name' on $tag after upload."
    }
}
if ($published.isDraft) {
    throw "publish-release: GitHub still reports $tag as a draft."
}
Write-Output "publish-release: assets on $tag`:"
foreach ($asset in @($published.assets)) {
    Write-Output ("  {0} ({1} bytes)" -f $asset.name, $asset.size)
}
