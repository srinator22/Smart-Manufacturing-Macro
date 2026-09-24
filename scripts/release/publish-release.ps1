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
#   (optional; passed to build-release.ps1), -DryRun or -WhatIf (stop after the interop guard and
#   print what would be uploaded, without calling GitHub), -SkipBuild (dry run only: package the
#   existing Release output), -AllowUntagged (dry run only: waive the tag check), and
#   -ReplacePublishedAssets (overwrite the assets of a release that is already published; the
#   documented repair path for a broken release, still from the tag with a clean tree).
# Outputs: the three release assets uploaded to v<Version>, the release published (non-draft), and
#   the asset list GitHub reports afterwards.
# Dependencies: Windows PowerShell 5.1 or later, git, gh (authenticated for -Repo), the .NET SDK
#   pinned by global.json, Inventor 2027 installed, build-release.ps1 beside this script.
# Assumptions: The draft release v<Version> already exists because the tag push ran
#   .github/workflows/release.yml. A real run is built from exactly the commit the tag names with a
#   clean tree: bin/ and dist/ are gitignored, so the clean-tree check cannot see stale Release output,
#   which is why a real run always rebuilds and never waives the tag check.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Repo = "srinator22/Smart-Manufacturing-Macro",

    [string]$DistRoot,

    [switch]$SkipBuild,

    [switch]$AllowUntagged,

    [switch]$ReplacePublishedAssets,

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
$assets = @($zipPath, $sumsPath, $installerPath)
$assetNames = @($assets | ForEach-Object { [System.IO.Path]::GetFileName($_) })
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

# The switches that weaken a check are dry-run only, and are rejected before anything is read, built
# or sent. -SkipBuild would package gitignored bin/Release output the clean-tree check cannot see, and
# -AllowUntagged would upload binaries that the tag does not name; both are exactly how a release ends
# up with assets nobody can reproduce from its tag.
if (-not $isDryRun) {
    if ($AllowUntagged) {
        throw "publish-release: -AllowUntagged is accepted only with -DryRun or -WhatIf; a real publish must be built from the commit $tag names. Check out $tag, or add -DryRun to exercise the build and the interop guard from this HEAD."
    }
    if ($SkipBuild) {
        throw "publish-release: -SkipBuild is accepted only with -DryRun or -WhatIf; bin/Release is gitignored, so the clean-tree check cannot prove existing output was built from $tag. Drop -SkipBuild so the package is rebuilt from the tag."
    }
}
if ($ReplacePublishedAssets -and $AllowUntagged) {
    throw "publish-release: -ReplacePublishedAssets cannot be combined with -AllowUntagged; replacing published assets requires HEAD at $tag and a clean tree."
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
        Write-Warning "publish-release: -AllowUntagged (dry run) - $reason Packaging HEAD $head anyway."
    }
    else {
        $refusals += $reason
    }
}
elseif ($head -ne $tagCommit) {
    $reason = "HEAD $head is not the commit $tagCommit that $tag names; check out the tag so the binaries match the release."
    if ($AllowUntagged) {
        Write-Warning "publish-release: -AllowUntagged (dry run) - $reason Packaging HEAD anyway; a real run refuses this."
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

# The release state is read before the build, so a release that must not be touched is refused
# without a multi-minute build. A dry run never calls GitHub, so it can only describe this step.
$releaseIsDraft = $true
if (-not $isDryRun) {
    $release = Invoke-Gh @("release", "view", $tag, "--repo", $Repo, "--json", "tagName,isDraft,assets") | ConvertFrom-Json
    $releaseIsDraft = [bool]$release.isDraft
    if (-not $releaseIsDraft) {
        if (-not $ReplacePublishedAssets) {
            throw "publish-release: $tag is already published (not a draft); refusing to replace its assets. Users may already have installed them. To repair a broken release in place, re-run from $tag with a clean tree and -ReplacePublishedAssets (ADR-0005 2026-09-24 amendment, docs/procedures/ship.md step 8)."
        }

        Write-Output "publish-release: -ReplacePublishedAssets - $tag is published; these assets will be overwritten:"
        $publishedAssets = @($release.assets)
        $overwritten = @($publishedAssets | Where-Object { $assetNames -contains $_.name })
        if ($overwritten.Count -eq 0) {
            Write-Output "  (none of $($assetNames -join ', ') is on $tag yet; they will be added)"
        }
        foreach ($asset in $overwritten) {
            Write-Output ("  {0} ({1} bytes)" -f $asset.name, $asset.size)
        }
        foreach ($asset in @($publishedAssets | Where-Object { $assetNames -notcontains $_.name })) {
            Write-Output ("  kept, not part of the package: {0} ({1} bytes)" -f $asset.name, $asset.size)
        }

        if (@($publishedAssets | Where-Object { $_.name -eq "SHA256SUMS.txt" }).Count -eq 0) {
            Write-Output "publish-release: current published digests: N/A - $tag has no SHA256SUMS.txt asset."
        }
        else {
            $publishedSumsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("publish-release-" + [guid]::NewGuid().ToString("N") + "-SHA256SUMS.txt")
            try {
                Invoke-Gh @("release", "download", $tag, "--repo", $Repo, "--pattern", "SHA256SUMS.txt", "--output", $publishedSumsPath, "--clobber") | Out-Null
                Write-Output "publish-release: current published SHA256SUMS.txt on $tag`:"
                foreach ($line in @(Get-Content -LiteralPath $publishedSumsPath)) {
                    if (-not [string]::IsNullOrWhiteSpace($line)) { Write-Output "  $line" }
                }
            }
            finally {
                if (Test-Path -LiteralPath $publishedSumsPath -PathType Leaf) {
                    Remove-Item -LiteralPath $publishedSumsPath -Force
                }
            }
        }
    }
    elseif ($ReplacePublishedAssets) {
        Write-Output "publish-release: $tag is still a draft; -ReplacePublishedAssets is not needed and changes nothing."
    }
}

if (-not (Test-Path -LiteralPath $interopPath -PathType Leaf)) {
    throw "publish-release: Autodesk.Inventor.Interop.dll is not at '$interopPath'. Run this on a machine with Inventor 2027 installed; without it every add-in compiles its entry point out."
}
Write-Output "publish-release: Inventor interop found at $interopPath"

# build-release.ps1 verifies -Version against Directory.Build.props and refuses any add-in DLL that
# lacks the interop reference or StandardAddInServer before it writes a package.
$buildArguments = @{ Version = $Version; DistRoot = $distRoot }
if ($SkipBuild) { $buildArguments["SkipBuild"] = $true }
if (-not $SkipBuild) {
    # A fresh checkout of the tag has no obj/ or project.assets.json, and build-release.ps1 builds with
    # --no-restore, so the locked restore has to happen here or the documented one-command publish fails
    # before packaging on any machine that does not happen to hold compatible restore state.
    Write-Output "publish-release: dotnet restore InventorScripts.sln --locked-mode"
    & dotnet restore (Join-Path $repoRoot "InventorScripts.sln") --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "publish-release: dotnet restore failed with exit code $LASTEXITCODE."
    }
}
& $buildScript @buildArguments
foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "publish-release: build-release.ps1 finished but '$asset' is missing."
    }
}

if ($isDryRun) {
    Write-Output "publish-release: DRY RUN - the interop guard passed. Would upload to $Repo release $tag`:"
    foreach ($asset in $assets) {
        $item = Get-Item -LiteralPath $asset
        Write-Output ("  {0} ({1} bytes)" -f $item.FullName, $item.Length)
    }
    Write-Output "publish-release: DRY RUN - would run: gh release view $tag --repo $Repo (refused unless a draft, or -ReplacePublishedAssets is given)"
    Write-Output "publish-release: DRY RUN - would run: gh release upload $tag <the three files above> --repo $Repo --clobber"
    Write-Output "publish-release: DRY RUN - would run: gh release edit $tag --repo $Repo --draft=false"
    if ($SkipBuild) {
        Write-Output "publish-release: DRY RUN - packaged the existing Release output (-SkipBuild); a real run rebuilds from $tag."
    }
    if ($refusals.Count -gt 0) {
        Write-Output "publish-release: DRY RUN - a real run would be REFUSED:"
        foreach ($reason in $refusals) { Write-Output "  - $reason" }
        exit 1
    }
    if ($AllowUntagged) {
        Write-Output "publish-release: DRY RUN - OK, but the tag check was waived by -AllowUntagged, which a real run does not accept."
        exit 0
    }
    Write-Output "publish-release: DRY RUN - OK; a real run would publish."
    exit 0
}

if (-not $releaseIsDraft) {
    Write-Output "publish-release: replacing the published assets of $tag with:"
    foreach ($line in @(Get-Content -LiteralPath $sumsPath)) {
        if (-not [string]::IsNullOrWhiteSpace($line)) { Write-Output "  $line" }
    }
}

Invoke-Gh (@("release", "upload", $tag) + $assets + @("--repo", $Repo, "--clobber")) | Out-Null
Write-Output "publish-release: uploaded $($assets.Count) assets to $tag"
if ($releaseIsDraft) {
    Invoke-Gh @("release", "edit", $tag, "--repo", $Repo, "--draft=false") | Out-Null
    Write-Output "publish-release: $tag is published"
}

$published = Invoke-Gh @("release", "view", $tag, "--repo", $Repo, "--json", "isDraft,assets") | ConvertFrom-Json
$names = @($published.assets | ForEach-Object { $_.name })
foreach ($name in $assetNames) {
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
