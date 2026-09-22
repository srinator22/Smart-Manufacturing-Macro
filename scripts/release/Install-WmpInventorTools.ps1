<#
.SYNOPSIS
    Installs, updates, or rolls back the WMP Inventor add-ins for the current user.

.DESCRIPTION
    Purpose: Provide the single supported install path defined by ADR-0005 - no executable ships, the
    script runs in memory, the download is verified against the published SHA-256 digest, and the
    previous install is archived instead of deleted so one command restores it.

    Inputs: a GitHub release of the pinned repository, or a local zip plus its SHA256SUMS file.
    Outputs: one folder per plugin under the Inventor 2027 per-user Addins root, one .addin manifest
    per plugin beside them, and installed.json under the state root.
    Dependencies: Windows PowerShell 5.1 or later and the .NET 10 Desktop runtime (x64).
    Assumptions: Inventor does not expand environment variables inside a .addin manifest, so the
    manifest is written here with the absolute path this script just copied to. Inventor locks add-in
    assemblies while it runs, so every mode refuses to proceed while Inventor.exe is alive.

.PARAMETER Repo
    The GitHub repository that publishes the release. Default srinator22/Smart-Manufacturing-Macro.

.PARAMETER Version
    A specific release version such as 0.6.0. Omit it to install the latest release.

.PARAMETER ZipPath
    Install from a local package zip instead of downloading. Requires -Sha256SumsPath.

.PARAMETER Sha256SumsPath
    The SHA256SUMS.txt that covers -ZipPath.

.PARAMETER AddinsRoot
    The Inventor 2027 per-user add-ins folder. Default %APPDATA%\Autodesk\Inventor 2027\Addins.

.PARAMETER StateRoot
    Where staging, previous installs, and installed.json live. Default %LOCALAPPDATA%\WMP\InventorTools.

.PARAMETER Rollback
    Restore the most recently archived previous install instead of installing.

.PARAMETER WaitForInventor
    Poll for up to 30 minutes until no Inventor.exe process remains, then continue.

.EXAMPLE
    irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1 | iex

.EXAMPLE
    .\Install-WmpInventorTools.ps1 -Version 0.6.0

.EXAMPLE
    .\Install-WmpInventorTools.ps1 -Rollback

.NOTES
    Exit codes: 0 success, 1 failure, 2 invalid arguments, 3 .NET 10 Desktop runtime missing,
    4 Inventor is running, 5 SHA-256 mismatch.
#>
[CmdletBinding()]
param(
    [string]$Repo = "srinator22/Smart-Manufacturing-Macro",

    [string]$Version = "",

    [string]$ZipPath = "",

    [string]$Sha256SumsPath = "",

    [string]$AddinsRoot = (Join-Path $env:APPDATA "Autodesk\Inventor 2027\Addins"),

    [string]$StateRoot = (Join-Path $env:LOCALAPPDATA "WMP\InventorTools"),

    [switch]$Rollback,

    [switch]$WaitForInventor
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$script:DotnetDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0"
$script:InventorWaitSeconds = 1800
# Flipped at the first real mutation of the Addins root so a later failure reports the true state.
# Archived is a separate fact: a first install touches the root without archiving anything, and
# pointing that user at -Rollback would promise a previous version that does not exist.
$script:AddinsRootTouched = $false
$script:AddinsRootArchived = $false

function Stop-Install {
    # Invoked from every failure path. `exit` inside `iex` (Invoke-Expression) terminates the
    # HOST PowerShell session, not just this script - a failed `irm ... | iex` one-liner would
    # close the user's window before they could read why. $PSCommandPath is empty only when the
    # script body is running in memory (iex), so that branch prints in red, sets $LASTEXITCODE for
    # scripts that inspect it, and `throw`s a terminating error to unwind just this script, leaving
    # the session open. File-mode execution (a real $PSCommandPath) keeps `exit <code>` so callers
    # such as the updater's apply step and test-release.sh still get the documented exit code.
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [int]$Code = 1
    )

    $fullMessage = "install: $Message"

    if ($PSCommandPath) {
        [Console]::Error.WriteLine($fullMessage)
        exit $Code
    }

    Write-Host $fullMessage -ForegroundColor Red
    if (-not $script:AddinsRootTouched) {
        Write-Host "Nothing was changed. Press Enter to close this message or run the command again after fixing the cause."
    }
    elseif ($script:AddinsRootArchived) {
        Write-Host "The install stopped part-way. The previous version is archived under the state root; run the command again, or run it with -Rollback to restore the previous version."
    }
    else {
        Write-Host "The install stopped part-way and there was no previous install to archive; run the command again after fixing the cause."
    }
    $global:LASTEXITCODE = $Code
    throw $fullMessage
}

function Get-Utc {
    return (Get-Date).ToUniversalTime()
}

function Get-UtcStamp {
    return (Get-Utc).ToString("yyyyMMddTHHmmssZ")
}

function Test-DesktopRuntime10 {
    # The add-ins are x64 only, so an x86 .NET 10 Desktop runtime does not satisfy them. `dotnet` on
    # PATH is not a safe witness: it may be the x86 host, which reports the x86 runtimes. The x64
    # host is therefore probed by path - on 64-bit Windows %ProgramFiles% is the x64 tree and
    # %ProgramFiles(x86)% is the x86 tree - and a host resolved under the x86 tree is never accepted.
    # -DotnetRoot keeps the probe testable against a fixture root; production calls take the default.
    param([string]$DotnetRoot = "")

    $candidateRoots = @()
    if ($DotnetRoot -ne "") {
        $candidateRoots += $DotnetRoot
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $candidateRoots += (Join-Path $env:ProgramFiles "dotnet")
        }
        if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
            $candidateRoots += $env:DOTNET_ROOT
        }
    }

    $x86Prefix = ""
    $x86Base = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($x86Base)) {
        $x86Prefix = [System.IO.Path]::GetFullPath($x86Base).TrimEnd('\') + '\'
    }

    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) { continue }

        $rootFull = [System.IO.Path]::GetFullPath($candidateRoot)
        if ($x86Prefix -ne "" -and
            ($rootFull.TrimEnd('\') + '\').StartsWith($x86Prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $sharedDirectory = Join-Path $rootFull "shared\Microsoft.WindowsDesktop.App"
        if (Test-Path -LiteralPath $sharedDirectory -PathType Container) {
            $installed = @(Get-ChildItem -LiteralPath $sharedDirectory -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "10.*" })
            if ($installed.Count -gt 0) {
                return $true
            }
        }

        $hostExe = Join-Path $rootFull "dotnet.exe"
        if (Test-Path -LiteralPath $hostExe -PathType Leaf) {
            $runtimes = & $hostExe --list-runtimes 2>$null
            if ($LASTEXITCODE -eq 0 -and $null -ne $runtimes) {
                foreach ($line in $runtimes) {
                    if ($line -like "Microsoft.WindowsDesktop.App 10.*") {
                        return $true
                    }
                }
            }
        }
    }

    return $false
}

function Test-PathSegment {
    # catalog.json and installed.json supply values that are used as ONE name directly under the
    # Addins root. Anything else - a separator, a drive letter, a relative marker, or a name Windows
    # re-interprets, such as a leading dot run - reaches outside that root, so it is rejected rather
    # than normalized. The zip's SHA-256 only proves the package is the one SHA256SUMS names; it
    # does not vouch for what the catalog inside it asks the installer to do.
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) { return $false }
    if ($Value -eq "." -or $Value -eq "..") { return $false }
    if ($Value.StartsWith(".")) { return $false }
    if ($Value.IndexOfAny([char[]]@('\', '/', ':')) -ge 0) { return $false }
    if ($Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) { return $false }
    if ($Value -ne [System.IO.Path]::GetFileName($Value)) { return $false }

    return $true
}

function Assert-PathSegment {
    param([string]$Value, [string]$Field, [string]$PluginId, [string]$Source)

    if (-not (Test-PathSegment -Value $Value)) {
        Stop-Install -Message ("$Source declares $Field '$Value' for plugin '$PluginId'; it must be a " +
            "single file or folder name - no path separators, drive letter, relative marker, or leading dot.") -Code 1
    }
}

function Get-InventorProcess {
    return @(Get-Process -Name "Inventor" -ErrorAction SilentlyContinue)
}

function Wait-InventorExit {
    $deadline = (Get-Date).AddSeconds($script:InventorWaitSeconds)
    while ((Get-InventorProcess).Count -gt 0) {
        if ((Get-Date) -gt $deadline) {
            Stop-Install -Message "Inventor was still running after 30 minutes; close Inventor and run the installer again." -Code 4
        }
        Write-Output "install: waiting for Inventor to close..."
        Start-Sleep -Seconds 10
    }
}

function Assert-InventorClosed {
    if ((Get-InventorProcess).Count -gt 0) {
        Stop-Install -Message "Inventor is running and locks the add-in files; close Inventor and run this again." -Code 4
    }
}

function Assert-SafeRoot {
    param([string]$Path, [string]$Label)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        Stop-Install -Message "$Label was empty." -Code 2
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $volumeRoot = [System.IO.Path]::GetPathRoot($resolved)
    if ([string]::Equals($resolved.TrimEnd('\'), $volumeRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Install -Message "$Label must not be a volume root: '$resolved'." -Code 2
    }

    return $resolved
}

function Read-InstalledState {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}

function Get-SumsEntry {
    param([string]$SumsFile, [string]$FileName)

    foreach ($line in (Get-Content -LiteralPath $SumsFile)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        $parts = $trimmed -split "\s+", 2
        if ($parts.Count -ne 2) { continue }
        if ([string]::Equals($parts[1].TrimStart('*'), $FileName, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $parts[0].ToLowerInvariant()
        }
    }

    return $null
}

function Get-SumsZipName {
    param([string]$SumsFile)

    foreach ($line in (Get-Content -LiteralPath $SumsFile)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        $parts = $trimmed -split "\s+", 2
        if ($parts.Count -ne 2) { continue }
        $name = $parts[1].TrimStart('*')
        if ($name -like "WmpInventorTools-*.zip") {
            return $name
        }
    }

    return $null
}

function Invoke-Download {
    param([string]$Uri, [string]$OutFile)

    try {
        Invoke-WebRequest -Uri $Uri -OutFile $OutFile -UseBasicParsing -Headers @{ "User-Agent" = "WmpInventorTools-Installer" }
    }
    catch {
        Stop-Install -Message "Download failed for '$Uri': $($_.Exception.Message)" -Code 1
    }
}

function Move-IntoArchive {
    param([string]$Source, [string]$ArchiveDirectory)

    if (-not (Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Path $ArchiveDirectory -Force | Out-Null
    $destination = Join-Path $ArchiveDirectory (Split-Path -Leaf $Source)
    if (Test-Path -LiteralPath $destination) {
        $destination = "$destination-$(Get-UtcStamp)"
    }
    Move-Item -LiteralPath $Source -Destination $destination -Force
}

function Write-Manifest {
    param([string]$TemplateFile, [string]$ManifestFile, [string]$AssemblyPath)

    if (-not (Test-Path -LiteralPath $TemplateFile -PathType Leaf)) {
        Stop-Install -Message "The package is missing the manifest template '$TemplateFile'." -Code 1
    }

    $escaped = [System.Security.SecurityElement]::Escape($AssemblyPath)
    $content = [System.IO.File]::ReadAllText($TemplateFile).Replace("__ASSEMBLY_PATH__", $escaped)
    if ($content.Contains("__ASSEMBLY_PATH__")) {
        Stop-Install -Message "The manifest template '$TemplateFile' did not accept the assembly path." -Code 1
    }

    [System.IO.File]::WriteAllText($ManifestFile, $content, [System.Text.UTF8Encoding]::new($false))
}

function Get-NewestPreviousInstall {
    param([string]$PreviousRoot)

    if (-not (Test-Path -LiteralPath $PreviousRoot -PathType Container)) {
        return $null
    }

    return Get-ChildItem -LiteralPath $PreviousRoot -Directory |
        Where-Object { @(Get-ChildItem -LiteralPath $_.FullName -Force).Count -gt 0 } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Show-Summary {
    param([string]$SummaryVersion, $Plugins, [string]$RollbackCommand)

    Write-Output ""
    Write-Output "WMP Inventor Tools $SummaryVersion is installed."
    Write-Output ""
    Write-Output ("  {0,-32} {1,-10} {2}" -f "Plugin", "Version", "Maturity")
    Write-Output ("  {0,-32} {1,-10} {2}" -f "------", "-------", "--------")
    foreach ($plugin in $Plugins) {
        $maturity = $plugin.maturity
        if ($maturity -eq "beta") {
            $maturity = "beta - not yet validated in live Inventor"
        }
        Write-Output ("  {0,-32} {1,-10} {2}" -f $plugin.displayName, $SummaryVersion, $maturity)
    }
    Write-Output ""
    Write-Output "Roll back with:"
    Write-Output "  $RollbackCommand"
    Write-Output ""
}

# --- argument validation -----------------------------------------------------

if (($ZipPath -ne "") -xor ($Sha256SumsPath -ne "")) {
    Stop-Install -Message "-ZipPath and -Sha256SumsPath must be supplied together." -Code 2
}
if ($Rollback -and $ZipPath -ne "") {
    Stop-Install -Message "-Rollback cannot be combined with -ZipPath." -Code 2
}

$resolvedAddinsRoot = Assert-SafeRoot -Path $AddinsRoot -Label "The add-ins root"
$resolvedStateRoot = Assert-SafeRoot -Path $StateRoot -Label "The state root"
$previousRoot = Join-Path $resolvedStateRoot "previous"
$installedStatePath = Join-Path $resolvedStateRoot "installed.json"
$rollbackCommand = "& ([scriptblock]::Create((irm https://github.com/$Repo/releases/latest/download/Install-WmpInventorTools.ps1))) -Rollback"

if ($WaitForInventor) {
    Wait-InventorExit
}

# --- rollback mode -----------------------------------------------------------

if ($Rollback) {
    Assert-InventorClosed

    $restore = Get-NewestPreviousInstall -PreviousRoot $previousRoot
    if ($null -eq $restore) {
        Stop-Install -Message "No archived install exists under '$previousRoot'; there is nothing to roll back to." -Code 1
    }

    $currentState = Read-InstalledState -Path $installedStatePath
    $currentVersion = "unknown"
    if ($null -ne $currentState -and $currentState.version) {
        $currentVersion = [string]$currentState.version
    }

    # Archiving only the names present in the archive would strand anything the NEWER release added:
    # a plugin introduced after the archived version would stay installed and loaded by Inventor while
    # installed.json reported the older version. The current installed.json is the authority on what
    # this install put in the Addins root, so it is archived first, then the archive's own names.
    # installed.json is local state, not a signed artifact, so a name it supplies is validated before
    # it is joined to the Addins root - otherwise a tampered state file moves arbitrary folders.
    $namesToArchive = @()
    if ($null -ne $currentState -and $currentState.plugins) {
        foreach ($recordedPlugin in @($currentState.plugins)) {
            $recordedId = ""
            if ($recordedPlugin.id) { $recordedId = [string]$recordedPlugin.id }
            if ($recordedPlugin.installDirectory) {
                Assert-PathSegment -Value ([string]$recordedPlugin.installDirectory) `
                    -Field "installDirectory" -PluginId $recordedId -Source "installed.json"
                $namesToArchive += [string]$recordedPlugin.installDirectory
            }
            if ($recordedPlugin.manifestName) {
                Assert-PathSegment -Value ([string]$recordedPlugin.manifestName) `
                    -Field "manifestName" -PluginId $recordedId -Source "installed.json"
                $namesToArchive += [string]$recordedPlugin.manifestName
            }
        }
    }

    if (-not (Test-PathSegment -Value $currentVersion)) {
        Write-Output "install: installed.json records an unusable version '$currentVersion'; archiving under 'unknown'."
        $currentVersion = "unknown"
    }
    $rolledBackDirectory = Join-Path $previousRoot "$currentVersion-rolledback-$(Get-UtcStamp)"
    New-Item -ItemType Directory -Path $rolledBackDirectory -Force | Out-Null

    foreach ($entry in @(Get-ChildItem -LiteralPath $restore.FullName -Force)) {
        if ($entry.Name -eq "installed.json") { continue }
        $namesToArchive += $entry.Name
    }

    $seenArchiveNames = @{}
    foreach ($name in $namesToArchive) {
        $key = $name.ToLowerInvariant()
        if ($seenArchiveNames.ContainsKey($key)) { continue }
        $seenArchiveNames[$key] = $true
        Move-IntoArchive -Source (Join-Path $resolvedAddinsRoot $name) -ArchiveDirectory $rolledBackDirectory
    }
    if (Test-Path -LiteralPath $installedStatePath) {
        Move-Item -LiteralPath $installedStatePath -Destination (Join-Path $rolledBackDirectory "installed.json") -Force
    }

    New-Item -ItemType Directory -Path $resolvedAddinsRoot -Force | Out-Null
    foreach ($entry in @(Get-ChildItem -LiteralPath $restore.FullName -Force)) {
        if ($entry.Name -eq "installed.json") {
            Move-Item -LiteralPath $entry.FullName -Destination $installedStatePath -Force
            continue
        }
        Move-Item -LiteralPath $entry.FullName -Destination (Join-Path $resolvedAddinsRoot $entry.Name) -Force
    }

    $restoredState = Read-InstalledState -Path $installedStatePath
    $restoredVersion = "unknown"
    if ($null -ne $restoredState -and $restoredState.version) {
        $restoredVersion = [string]$restoredState.version
    }

    Write-Output "install: restored '$($restore.Name)' into '$resolvedAddinsRoot'."
    Write-Output "install: the replaced install is archived at '$rolledBackDirectory'."
    Write-Output "install: WMP Inventor Tools $restoredVersion is active."
}
else {
    # --- preflight ---------------------------------------------------------------

    if (-not (Test-DesktopRuntime10)) {
        Stop-Install -Message ("The .NET 10 Desktop runtime (x64) is required; the x86 runtime does not load " +
            "these x64 add-ins. Install the x64 runtime from $script:DotnetDownloadUrl and run this again.") -Code 3
    }

    Assert-InventorClosed

    # --- acquire the package -----------------------------------------------------

    $stagingRoot = Join-Path $resolvedStateRoot "staging"
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

    if ($ZipPath -ne "") {
        if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
            Stop-Install -Message "The package zip '$ZipPath' does not exist." -Code 2
        }
        if (-not (Test-Path -LiteralPath $Sha256SumsPath -PathType Leaf)) {
            Stop-Install -Message "The digest file '$Sha256SumsPath' does not exist." -Code 2
        }

        $packageZip = [System.IO.Path]::GetFullPath($ZipPath)
        $packageSums = [System.IO.Path]::GetFullPath($Sha256SumsPath)
        $zipName = [System.IO.Path]::GetFileName($packageZip)
        $stagedVersion = "local-$(Get-UtcStamp)"
        if ($zipName -match "^WmpInventorTools-(.+)\.zip$") {
            $stagedVersion = $Matches[1]
        }
        $stagingDirectory = Join-Path $stagingRoot $stagedVersion
        New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    }
    else {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        if ($Version -ne "") {
            $baseUri = "https://github.com/$Repo/releases/download/v$Version"
        }
        else {
            $baseUri = "https://github.com/$Repo/releases/latest/download"
        }

        $stagingDirectory = Join-Path $stagingRoot "incoming-$(Get-UtcStamp)"
        New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

        $packageSums = Join-Path $stagingDirectory "SHA256SUMS.txt"
        Invoke-Download -Uri "$baseUri/SHA256SUMS.txt" -OutFile $packageSums

        $zipName = Get-SumsZipName -SumsFile $packageSums
        if ($null -eq $zipName) {
            Stop-Install -Message "SHA256SUMS.txt from '$baseUri' does not list a WmpInventorTools zip." -Code 1
        }

        $stagedVersion = "unknown"
        if ($zipName -match "^WmpInventorTools-(.+)\.zip$") {
            $stagedVersion = $Matches[1]
        }
        $versionedStaging = Join-Path $stagingRoot $stagedVersion
        if (Test-Path -LiteralPath $versionedStaging) {
            Move-IntoArchive -Source $versionedStaging -ArchiveDirectory (Join-Path $stagingRoot "superseded")
        }
        Move-Item -LiteralPath $stagingDirectory -Destination $versionedStaging -Force
        $stagingDirectory = $versionedStaging
        $packageSums = Join-Path $stagingDirectory "SHA256SUMS.txt"

        $packageZip = Join-Path $stagingDirectory $zipName
        Invoke-Download -Uri "$baseUri/$zipName" -OutFile $packageZip
    }

    # --- verify ------------------------------------------------------------------

    $expectedHash = Get-SumsEntry -SumsFile $packageSums -FileName $zipName
    if ($null -eq $expectedHash) {
        Stop-Install -Message "'$packageSums' has no digest for '$zipName'." -Code 5
    }

    $actualHash = (Get-FileHash -LiteralPath $packageZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        if ($ZipPath -eq "") {
            # Only a download is ours to quarantine; a caller-supplied zip is left untouched.
            $quarantine = Join-Path $stagingDirectory "rejected-$(Get-UtcStamp).zip"
            Move-Item -LiteralPath $packageZip -Destination $quarantine -Force
            Stop-Install -Message "SHA-256 mismatch for '$zipName': expected $expectedHash, got $actualHash. The rejected download is quarantined at '$quarantine'; do not use it." -Code 5
        }
        Stop-Install -Message "SHA-256 mismatch for '$packageZip': expected $expectedHash, got $actualHash. Do not install this file." -Code 5
    }

    Unblock-File -LiteralPath $packageZip
    Unblock-File -LiteralPath $packageSums

    $packageDirectory = Join-Path $stagingDirectory "package"
    if (Test-Path -LiteralPath $packageDirectory) {
        Move-IntoArchive -Source $packageDirectory -ArchiveDirectory (Join-Path $stagingDirectory "superseded")
    }
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $packageZip -DestinationPath $packageDirectory -Force
    Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | ForEach-Object { Unblock-File -LiteralPath $_.FullName }

    $catalogPath = Join-Path $packageDirectory "catalog.json"
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        Stop-Install -Message "The package does not contain catalog.json." -Code 1
    }
    $packageInstallerPath = Join-Path $packageDirectory "Install-WmpInventorTools.ps1"
    if (-not (Test-Path -LiteralPath $packageInstallerPath -PathType Leaf)) {
        Stop-Install -Message "The package does not contain Install-WmpInventorTools.ps1." -Code 1
    }
    $catalog = Get-Content -Raw -LiteralPath $catalogPath | ConvertFrom-Json
    if ($null -eq $catalog.version -or @($catalog.plugins).Count -eq 0) {
        Stop-Install -Message "catalog.json is malformed; it must declare a version and at least one plugin." -Code 1
    }
    $releaseVersion = [string]$catalog.version

    # Every catalog value that becomes a path segment is checked here, before the Addins root is
    # touched, so a hostile or corrupt catalog cannot stage, install, or archive outside that root.
    foreach ($plugin in @($catalog.plugins)) {
        $catalogId = ""
        if ($plugin.id) { $catalogId = [string]$plugin.id }
        Assert-PathSegment -Value $catalogId -Field "id" -PluginId $catalogId -Source "catalog.json"
        Assert-PathSegment -Value ([string]$plugin.installDirectory) `
            -Field "installDirectory" -PluginId $catalogId -Source "catalog.json"
        Assert-PathSegment -Value ([string]$plugin.manifestName) `
            -Field "manifestName" -PluginId $catalogId -Source "catalog.json"
        # The packager always stages the template as templates/<id>.addin.template; the catalog value
        # is a relative path, so it is checked against that derivation instead of being joined blindly.
        $expectedTemplate = "templates/$catalogId.addin.template"
        if ([string]$plugin.addinTemplate -ne $expectedTemplate) {
            Stop-Install -Message "catalog.json entry '$catalogId' declares addinTemplate '$($plugin.addinTemplate)'; expected '$expectedTemplate'." -Code 1
        }
    }

    # --- archive the current install --------------------------------------------

    $previousState = Read-InstalledState -Path $installedStatePath
    $previousVersion = "unknown"
    if ($null -ne $previousState -and $previousState.version) {
        $previousVersion = [string]$previousState.version
    }
    if (-not (Test-PathSegment -Value $previousVersion)) {
        Write-Output "install: installed.json records an unusable version '$previousVersion'; archiving under 'unknown'."
        $previousVersion = "unknown"
    }

    $archiveDirectory = Join-Path $previousRoot $previousVersion
    if ((Test-Path -LiteralPath $archiveDirectory) -and
        @(Get-ChildItem -LiteralPath $archiveDirectory -Force).Count -gt 0) {
        $archiveDirectory = Join-Path $previousRoot "$previousVersion-$(Get-UtcStamp)"
    }

    $archivedAnything = $false

    # A release that drops a plugin leaves its folder and .addin manifest behind: they are absent
    # from the new catalog, so the loop below never looks at them, and Inventor would keep loading a
    # plugin that installed.json no longer records. The current installed.json is the authority on
    # what the last install put in the Addins root, so anything it lists that this release no longer
    # ships is archived first, into the same archive directory as the replaced files.
    $catalogIds = @{}
    foreach ($plugin in @($catalog.plugins)) {
        $catalogIds[([string]$plugin.id).ToLowerInvariant()] = $true
    }
    if ($null -ne $previousState -and $previousState.plugins) {
        foreach ($recordedPlugin in @($previousState.plugins)) {
            $recordedId = ""
            if ($recordedPlugin.id) { $recordedId = [string]$recordedPlugin.id }
            if ($recordedId -ne "" -and $catalogIds.ContainsKey($recordedId.ToLowerInvariant())) {
                continue
            }

            $removedNames = @()
            if ($recordedPlugin.installDirectory) {
                Assert-PathSegment -Value ([string]$recordedPlugin.installDirectory) `
                    -Field "installDirectory" -PluginId $recordedId -Source "installed.json"
                $removedNames += [string]$recordedPlugin.installDirectory
            }
            if ($recordedPlugin.manifestName) {
                Assert-PathSegment -Value ([string]$recordedPlugin.manifestName) `
                    -Field "manifestName" -PluginId $recordedId -Source "installed.json"
                $removedNames += [string]$recordedPlugin.manifestName
            }

            $removedAnything = $false
            foreach ($removedName in $removedNames) {
                $removedPath = Join-Path $resolvedAddinsRoot $removedName
                if (Test-Path -LiteralPath $removedPath) {
                    $script:AddinsRootTouched = $true
                    Move-IntoArchive -Source $removedPath -ArchiveDirectory $archiveDirectory
                    $archivedAnything = $true
                    $script:AddinsRootArchived = $true
                    $removedAnything = $true
                }
            }
            if ($removedAnything) {
                Write-Output "Archived plugin no longer in this release: $recordedId"
            }
        }
    }

    foreach ($plugin in @($catalog.plugins)) {
        $existingDirectory = Join-Path $resolvedAddinsRoot $plugin.installDirectory
        $existingManifest = Join-Path $resolvedAddinsRoot $plugin.manifestName
        if (Test-Path -LiteralPath $existingDirectory) {
            $script:AddinsRootTouched = $true
            Move-IntoArchive -Source $existingDirectory -ArchiveDirectory $archiveDirectory
            $archivedAnything = $true
            $script:AddinsRootArchived = $true
        }
        if (Test-Path -LiteralPath $existingManifest -PathType Leaf) {
            $script:AddinsRootTouched = $true
            Move-IntoArchive -Source $existingManifest -ArchiveDirectory $archiveDirectory
            $archivedAnything = $true
            $script:AddinsRootArchived = $true
        }
    }
    if ($archivedAnything -and (Test-Path -LiteralPath $installedStatePath -PathType Leaf)) {
        Copy-Item -LiteralPath $installedStatePath -Destination (Join-Path $archiveDirectory "installed.json") -Force
    }

    # --- install -----------------------------------------------------------------

    $script:AddinsRootTouched = $true
    New-Item -ItemType Directory -Path $resolvedAddinsRoot -Force | Out-Null
    $installedPlugins = @()
    foreach ($plugin in @($catalog.plugins)) {
        $sourceDirectory = Join-Path $packageDirectory $plugin.installDirectory
        if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
            Stop-Install -Message "The package is missing the plugin folder '$($plugin.installDirectory)'." -Code 1
        }

        $targetDirectory = Join-Path $resolvedAddinsRoot $plugin.installDirectory
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceDirectory "*") -Destination $targetDirectory -Recurse -Force

        $installedAssembly = Join-Path $targetDirectory $plugin.assembly
        if (-not (Test-Path -LiteralPath $installedAssembly -PathType Leaf)) {
            Stop-Install -Message "The plugin assembly '$installedAssembly' was not installed." -Code 1
        }

        Write-Manifest `
            -TemplateFile (Join-Path (Join-Path $packageDirectory "templates") "$($plugin.id).addin.template") `
            -ManifestFile (Join-Path $resolvedAddinsRoot $plugin.manifestName) `
            -AssemblyPath $installedAssembly

        $installedPlugins += [ordered]@{
            id               = $plugin.id
            maturity         = $plugin.maturity
            installDirectory = $plugin.installDirectory
            manifestName     = $plugin.manifestName
        }
    }

    $installedState = [ordered]@{
        version     = $releaseVersion
        installedUtc = (Get-Utc).ToString("yyyy-MM-ddTHH:mm:ssZ")
        plugins     = @($installedPlugins)
    }
    New-Item -ItemType Directory -Path $resolvedStateRoot -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $installedStatePath,
        (ConvertTo-Json -InputObject $installedState -Depth 6),
        [System.Text.UTF8Encoding]::new($false))

    # Rollback needs this script on disk (PhysicalInstallState.FindInstaller), which a one-liner
    # `irm | iex` install never leaves behind on its own - every install persists its own copy here so
    # the first rollback anyone runs always has an installer to run.
    $persistedInstallerPath = Join-Path $resolvedStateRoot "Install-WmpInventorTools.ps1"
    Copy-Item -LiteralPath $packageInstallerPath -Destination $persistedInstallerPath -Force

    Show-Summary -SummaryVersion $releaseVersion -Plugins @($catalog.plugins) -RollbackCommand $rollbackCommand
    Write-Output "Add-ins root: $resolvedAddinsRoot"
    Write-Output "State root:   $resolvedStateRoot"
    Write-Output "Installer kept at: $persistedInstallerPath"
    if ($archivedAnything) {
        Write-Output "Previous install archived at: $archiveDirectory"
    }
}

# Same hazard as Stop-Install: `exit` inside `irm ... | iex` terminates the HOST session, so a
# successful one-liner install would close the user's window along with its own summary. File mode
# keeps `exit 0` for callers that read the exit code (the updater's apply step, test-release.sh);
# in-memory mode falls off the end of the script instead. Both success paths reach this single line,
# which is why rollback is an `if` branch rather than an early `exit`.
if ($PSCommandPath) { exit 0 }
