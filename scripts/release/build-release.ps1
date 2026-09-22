# build-release.ps1 - Packages every projects/*/plugin.json add-in into one release zip.
#
# Purpose: Produce the single distributable that ADR-0005 defines - dist/WmpInventorTools-<version>.zip
#   containing one folder per plugin, the raw .addin templates, catalog.json, and the installer - plus
#   dist/SHA256SUMS.txt.
# Inputs: -Version (must equal Directory.Build.props VersionPrefix), the plugin catalog files under
#   -ProjectsRoot (default: <repo>\projects; overridable so tests can point at a fixture tree), and the
#   Release build output of each add-in project.
# Outputs: dist/WmpInventorTools-<version>/, dist/WmpInventorTools-<version>.zip, dist/SHA256SUMS.txt.
# Dependencies: Windows PowerShell 5.1 or later, the .NET SDK pinned by global.json.
# Assumptions: The .addin manifest carries the absolute assembly path, and Inventor does not expand
#   environment variables inside it, so the manifest cannot be written at package time. The template is
#   shipped verbatim and the installer substitutes the real per-user path it just wrote to.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ProjectsRoot,

    [string]$DistRoot,

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
if ([string]::IsNullOrWhiteSpace($ProjectsRoot)) {
    $ProjectsRoot = Join-Path $repoRoot "projects"
}
$ProjectsRoot = [System.IO.Path]::GetFullPath($ProjectsRoot)
$solutionPath = Join-Path $repoRoot "InventorScripts.sln"
if ([string]::IsNullOrWhiteSpace($DistRoot)) {
    $DistRoot = Join-Path $repoRoot "dist"
}
$distRoot = [System.IO.Path]::GetFullPath($DistRoot)
$stageRoot = Join-Path $distRoot "WmpInventorTools-$Version"
$zipPath = Join-Path $distRoot "WmpInventorTools-$Version.zip"
$sumsPath = Join-Path $distRoot "SHA256SUMS.txt"
$installerSource = Join-Path $PSScriptRoot "Install-WmpInventorTools.ps1"
$requiredFields = @(
    "id", "displayName", "description", "maturity", "addinProject",
    "assembly", "addinTemplate", "installDirectory", "ribbonPanel", "commands", "homepage")

function Get-VersionPrefix {
    param([string]$PropsPath)

    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) {
        throw "Directory.Build.props not found at '$PropsPath'."
    }

    [xml]$props = Get-Content -Raw -LiteralPath $PropsPath
    $nodes = @($props.SelectNodes("/Project/PropertyGroup/VersionPrefix"))
    if ($nodes.Count -ne 1) {
        throw "Directory.Build.props must declare exactly one VersionPrefix; found $($nodes.Count)."
    }

    return [string]$nodes[0].InnerText
}

function Test-PathSegment {
    # installDirectory and the derived manifest name are used as ONE name under the stage root here
    # and, at install time, under the Inventor Addins root. Anything else - a separator, a drive
    # letter, a relative marker, or a name Windows re-interprets, such as a leading dot run - reaches
    # outside both roots, so it is rejected rather than normalized. The installer repeats this check
    # on catalog.json because a package's SHA-256 proves only which file it is, not what it asks for.
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) { return $false }
    if ($Value -eq "." -or $Value -eq "..") { return $false }
    if ($Value.StartsWith(".")) { return $false }
    if ($Value.IndexOfAny([char[]]@('\', '/', ':')) -ge 0) { return $false }
    if ($Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) { return $false }
    if ($Value -ne [System.IO.Path]::GetFileName($Value)) { return $false }

    return $true
}

$versionPrefix = Get-VersionPrefix -PropsPath (Join-Path $repoRoot "Directory.Build.props")
if ($Version -ne $versionPrefix) {
    throw "Requested version '$Version' does not match Directory.Build.props VersionPrefix '$versionPrefix'."
}

# Every project that compiles an add-in host must be in the catalog; a silently skipped project would
# ship a release that installs fewer plugins than the workspace builds.
$projectDirectories = @(Get-ChildItem -LiteralPath $ProjectsRoot -Directory | Sort-Object FullName)
foreach ($projectDirectory in $projectDirectories) {
    $hasAddInProject = @(Get-ChildItem -LiteralPath $projectDirectory.FullName -Recurse -Filter "*.AddIn.csproj").Count -gt 0
    $hasCatalog = Test-Path -LiteralPath (Join-Path $projectDirectory.FullName "plugin.json") -PathType Leaf
    if ($hasAddInProject -and -not $hasCatalog) {
        throw "'$($projectDirectory.FullName)' builds an add-in but has no plugin.json; add one so the release packages it."
    }
}

$catalogFiles = @($projectDirectories |
    ForEach-Object { Join-Path $_.FullName "plugin.json" } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Sort-Object)
if ($catalogFiles.Count -eq 0) {
    throw "No plugin.json catalog files were found under '$ProjectsRoot'."
}

$plugins = @()
foreach ($catalogFile in $catalogFiles) {
    $projectDirectory = Split-Path -Parent $catalogFile
    $plugin = Get-Content -Raw -LiteralPath $catalogFile | ConvertFrom-Json

    foreach ($field in $requiredFields) {
        if (-not ($plugin.PSObject.Properties.Name -contains $field)) {
            throw "'$catalogFile' is missing the required field '$field'."
        }
    }
    if (@("beta", "stable") -notcontains $plugin.maturity) {
        throw "'$catalogFile' declares maturity '$($plugin.maturity)'; only 'beta' and 'stable' are valid."
    }

    $addinProject = Join-Path $projectDirectory ($plugin.addinProject -replace "/", "\")
    if (-not (Test-Path -LiteralPath $addinProject -PathType Leaf)) {
        throw "'$catalogFile' points at a missing add-in project '$addinProject'."
    }
    $templatePath = Join-Path $projectDirectory ($plugin.addinTemplate -replace "/", "\")
    if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
        throw "'$catalogFile' points at a missing manifest template '$templatePath'."
    }

    $manifestName = [System.IO.Path]::GetFileNameWithoutExtension($templatePath)
    if (-not $manifestName.EndsWith(".addin", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "'$templatePath' must be named '<manifest>.addin.template'."
    }

    $plugins += [pscustomobject]@{
        Definition       = $plugin
        ProjectDirectory = $projectDirectory
        OutputDirectory  = Join-Path (Split-Path -Parent $addinProject) "bin\Release\net10.0-windows"
        TemplatePath     = $templatePath
        ManifestName     = $manifestName
        CatalogFile      = $catalogFile
    }
}

# A shared id, installDirectory, or derived manifest name across catalogs is not a schema error the
# per-field checks above catch, but it silently corrupts staging: two plugins would overwrite each
# other's staged template or merge their binaries into one installDirectory. Catch it before any
# build or staging happens so an invalid catalog set never produces a partial dist/ output.
$idPattern = '^[a-z0-9][a-z0-9-]*$'
$idOwners = @{}
$installDirectoryOwners = @{}
$manifestNameOwners = @{}
foreach ($plugin in $plugins) {
    $definition = $plugin.Definition
    $catalogFile = $plugin.CatalogFile

    if ($definition.id -notmatch $idPattern) {
        throw "'$catalogFile' declares id '$($definition.id)' which is not a valid id; ids must match '$idPattern'."
    }

    if (-not (Test-PathSegment -Value ([string]$definition.installDirectory))) {
        throw "'$catalogFile' declares installDirectory '$($definition.installDirectory)' for plugin '$($definition.id)'; it must be a single folder name - no path separators, drive letter, relative marker, or leading dot."
    }

    if (-not (Test-PathSegment -Value ([string]$plugin.ManifestName))) {
        throw "'$catalogFile' derives the manifest name '$($plugin.ManifestName)' from '$($plugin.TemplatePath)' for plugin '$($definition.id)'; the manifest name must be a single file name - no path separators, drive letter, relative marker, or leading dot."
    }

    if ($idOwners.ContainsKey($definition.id)) {
        throw "Duplicate plugin id '$($definition.id)' found in '$($idOwners[$definition.id])' and '$catalogFile'."
    }
    $idOwners[$definition.id] = $catalogFile

    if ($installDirectoryOwners.ContainsKey($definition.installDirectory)) {
        throw "Duplicate installDirectory '$($definition.installDirectory)' found in '$($installDirectoryOwners[$definition.installDirectory])' and '$catalogFile'."
    }
    $installDirectoryOwners[$definition.installDirectory] = $catalogFile

    if ($manifestNameOwners.ContainsKey($plugin.ManifestName)) {
        throw "Duplicate manifest name '$($plugin.ManifestName)' found in '$($manifestNameOwners[$plugin.ManifestName])' and '$catalogFile'."
    }
    $manifestNameOwners[$plugin.ManifestName] = $catalogFile
}

if (-not $SkipBuild) {
    Write-Output "build-release: dotnet build $solutionPath -c Release --no-restore"
    & dotnet build $solutionPath -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$templateStage = Join-Path $stageRoot "templates"
New-Item -ItemType Directory -Path $templateStage -Force | Out-Null

$catalogPlugins = @()
foreach ($plugin in $plugins) {
    $definition = $plugin.Definition
    $sourceAssembly = Join-Path $plugin.OutputDirectory $definition.assembly
    if (-not (Test-Path -LiteralPath $sourceAssembly -PathType Leaf)) {
        throw "Release output '$sourceAssembly' is missing; build the solution in Release first."
    }

    $pluginStage = Join-Path $stageRoot $definition.installDirectory
    New-Item -ItemType Directory -Path $pluginStage -Force | Out-Null
    Copy-Item -Path (Join-Path $plugin.OutputDirectory "*") -Destination $pluginStage -Recurse -Force

    $stagedTemplateName = "$($definition.id).addin.template"
    Copy-Item -LiteralPath $plugin.TemplatePath -Destination (Join-Path $templateStage $stagedTemplateName) -Force

    $catalogPlugins += [ordered]@{
        id               = $definition.id
        displayName      = $definition.displayName
        description      = $definition.description
        maturity         = $definition.maturity
        installDirectory = $definition.installDirectory
        assembly         = $definition.assembly
        addinTemplate    = "templates/$stagedTemplateName"
        manifestName     = $plugin.ManifestName
        ribbonPanel      = $definition.ribbonPanel
        commands         = @($definition.commands)
    }

    Write-Output "build-release: staged $($definition.id) -> $($definition.installDirectory) [$($definition.maturity)]"
}

$catalog = [ordered]@{
    version     = $Version
    releasedUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    plugins     = @($catalogPlugins)
}
$catalogJson = ConvertTo-Json -InputObject $catalog -Depth 6
[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot "catalog.json"),
    $catalogJson,
    [System.Text.UTF8Encoding]::new($false))

if (-not (Test-Path -LiteralPath $installerSource -PathType Leaf)) {
    throw "Installer script not found at '$installerSource'."
}
Copy-Item -LiteralPath $installerSource -Destination (Join-Path $stageRoot "Install-WmpInventorTools.ps1") -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

$sumLines = @()
foreach ($artifact in @($zipPath, (Join-Path $stageRoot "Install-WmpInventorTools.ps1"))) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $sumLines += "$hash  $([System.IO.Path]::GetFileName($artifact))"
}
[System.IO.File]::WriteAllText(
    $sumsPath,
    ($sumLines -join "`n") + "`n",
    [System.Text.UTF8Encoding]::new($false))

Write-Output "build-release: $zipPath"
Write-Output "build-release: $sumsPath"
Write-Output "build-release: OK ($($catalogPlugins.Count) plugins, version $Version)"
