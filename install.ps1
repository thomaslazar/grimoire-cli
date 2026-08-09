# Fetches from GitHub Releases. There are no releases yet (no tags cut), so
# this script has nothing to install until the first `v*` release ships.

$ErrorActionPreference = "Stop"

$Repo = "thomaslazar/grimoire-cli"
$InstallDir = if ($env:GRIMOIRE_CLI_INSTALL_DIR) { $env:GRIMOIRE_CLI_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA "grimoire-cli" }
$Version = $env:GRIMOIRE_CLI_VERSION

# Detect architecture
$Arch = $env:PROCESSOR_ARCHITECTURE
switch ($Arch) {
    "AMD64" { $Rid = "win-x64" }
    "ARM64" { $Rid = "win-arm64" }
    default { Write-Error "Unsupported architecture: $Arch"; exit 1 }
}

# Resolve version
if (-not $Version) {
    $Release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
    $Version = $Release.tag_name
}

Write-Host "Installing grimoire-cli $Version ($Rid)..."

# Download
$DownloadUrl = "https://github.com/$Repo/releases/download/$Version/grimoire-cli-${Rid}.exe"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$BinaryPath = Join-Path $InstallDir "grimoire-cli.exe"
Invoke-WebRequest -Uri $DownloadUrl -OutFile $BinaryPath -UseBasicParsing

# Add to user PATH if not already present
$UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
$PathEntries = if ($UserPath) { $UserPath -split ";" } else { @() }
if ($InstallDir -notin $PathEntries) {
    $NewPath = if ($UserPath) { "$UserPath;$InstallDir" } else { $InstallDir }
    [Environment]::SetEnvironmentVariable("Path", $NewPath, "User")
    $env:Path = "$env:Path;$InstallDir"
    Write-Host "Added $InstallDir to user PATH."
}

# Verify
& $BinaryPath --version
Write-Host "grimoire-cli installed to $BinaryPath"
