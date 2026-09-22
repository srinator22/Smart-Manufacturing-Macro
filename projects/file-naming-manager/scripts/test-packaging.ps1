# test-packaging.ps1 - Verifies install and uninstall against an isolated temporary Addins root.
$ErrorActionPreference = "Stop"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "inventor-scripts-package-$([guid]::NewGuid())"
$resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

if (-not $resolvedTestRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Temporary package test root escaped the system temporary directory."
}

try {
    & (Join-Path $PSScriptRoot "install-addin.ps1") -Configuration Debug -AddinsRoot $resolvedTestRoot

    $manifestPath = Join-Path $resolvedTestRoot "Autodesk.FileNamingManager.Inventor.addin"
    $assemblyPath = Join-Path $resolvedTestRoot "FileNamingManager\FileNamingManager.AddIn.dll"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package test did not create the add-in manifest."
    }
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Package test did not copy the add-in assembly."
    }

    [xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
    if ($manifest.Addin.ClassId -ne "{BB7F1BFF-D45E-440A-897B-F70E6A4ADE67}") {
        throw "Package test found an unexpected add-in ClassId."
    }
    if ($manifest.Addin.SupportedSoftwareVersionEqualTo -ne "31..") {
        throw "Package test found an unexpected Inventor compatibility boundary."
    }
    if (-not [string]::Equals(
        [System.IO.Path]::GetFullPath($manifest.Addin.Assembly),
        [System.IO.Path]::GetFullPath($assemblyPath),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package test manifest does not point to the installed assembly."
    }

    & (Join-Path $PSScriptRoot "uninstall-addin.ps1") -AddinsRoot $resolvedTestRoot
    if (Test-Path -LiteralPath $manifestPath) {
        throw "Package test uninstall left the manifest behind."
    }
    if (Test-Path -LiteralPath (Join-Path $resolvedTestRoot "FileNamingManager")) {
        throw "Package test uninstall left the binary directory behind."
    }

    Write-Output "packaging: OK"
}
finally {
    if (Test-Path -LiteralPath $resolvedTestRoot) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}