# uninstall-addin.ps1 - Removes only this add-in's verified per-user targets.
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$AddinsRoot = (Join-Path $env:APPDATA "Autodesk\Inventor 2027\Addins")
)

$ErrorActionPreference = "Stop"
$resolvedAddinsRoot = [System.IO.Path]::GetFullPath($AddinsRoot)
$volumeRoot = [System.IO.Path]::GetPathRoot($resolvedAddinsRoot)
if ([string]::Equals($resolvedAddinsRoot.TrimEnd('\'), $volumeRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The add-ins root must not be a volume root: '$resolvedAddinsRoot'."
}

$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $resolvedAddinsRoot "WmpToolsManager"))
$installedManifest = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedAddinsRoot "Autodesk.WmpToolsManager.Inventor.addin"))

if (-not [string]::Equals(
    [System.IO.Directory]::GetParent($installDirectory).FullName,
    $resolvedAddinsRoot.TrimEnd('\'),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove an add-in directory outside '$resolvedAddinsRoot'."
}
if (-not [string]::Equals(
    [System.IO.Path]::GetDirectoryName($installedManifest),
    $resolvedAddinsRoot.TrimEnd('\'),
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a manifest outside '$resolvedAddinsRoot'."
}

if ((Test-Path -LiteralPath $installedManifest) -and
    $PSCmdlet.ShouldProcess($installedManifest, "Remove WMP Tools Manager manifest")) {
    Remove-Item -LiteralPath $installedManifest -Force
}
if ((Test-Path -LiteralPath $installDirectory) -and
    $PSCmdlet.ShouldProcess($installDirectory, "Remove WMP Tools Manager binaries")) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Output "WMP Tools Manager is removed from '$resolvedAddinsRoot'."
