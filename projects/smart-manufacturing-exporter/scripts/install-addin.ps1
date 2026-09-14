# install-addin.ps1 - Copies one built configuration into Inventor's per-user add-in location.
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$AddinsRoot = (Join-Path $env:APPDATA "Autodesk\Inventor 2027\Addins")
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourceDirectory = Join-Path $projectRoot "src\SmartManufacturingExporter.AddIn\bin\$Configuration\net10.0-windows"
$sourceAssembly = Join-Path $sourceDirectory "SmartManufacturingExporter.AddIn.dll"
$manifestTemplate = Join-Path $projectRoot "packaging\Autodesk.SmartManufacturingExporter.Inventor.addin.template"

if (-not (Test-Path -LiteralPath $sourceAssembly -PathType Leaf)) {
    throw "Built add-in not found at '$sourceAssembly'. Build the $Configuration configuration before installing."
}
if (-not (Test-Path -LiteralPath $manifestTemplate -PathType Leaf)) {
    throw "Add-in manifest template not found at '$manifestTemplate'."
}

$resolvedAddinsRoot = [System.IO.Path]::GetFullPath($AddinsRoot)
$volumeRoot = [System.IO.Path]::GetPathRoot($resolvedAddinsRoot)
if ([string]::Equals($resolvedAddinsRoot.TrimEnd('\'), $volumeRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The add-ins root must not be a volume root: '$resolvedAddinsRoot'."
}

$installDirectory = Join-Path $resolvedAddinsRoot "SmartManufacturingExporter"
$installedAssembly = Join-Path $installDirectory "SmartManufacturingExporter.AddIn.dll"
$installedManifest = Join-Path $resolvedAddinsRoot "Autodesk.SmartManufacturingExporter.Inventor.addin"

if ($PSCmdlet.ShouldProcess($installDirectory, "Install Smart Manufacturing Exporter for the current user")) {
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $installDirectory -Recurse -Force

    $escapedAssemblyPath = [System.Security.SecurityElement]::Escape($installedAssembly)
    $manifest = [System.IO.File]::ReadAllText($manifestTemplate).Replace("__ASSEMBLY_PATH__", $escapedAssemblyPath)
    [System.IO.File]::WriteAllText(
        $installedManifest,
        $manifest,
        [System.Text.UTF8Encoding]::new($false))
}

Write-Output "Installed assembly: $installedAssembly"
Write-Output "Installed manifest: $installedManifest"
