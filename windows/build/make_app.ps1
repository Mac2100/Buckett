<#
.SYNOPSIS
  Builds Buckett for Windows, packaging it as a portable ZIP and an installer.

.DESCRIPTION
  Produces, in windows/dist/:
    Buckett/                          the published app (self-contained, no .NET install needed)
    Buckett-<version>-win-x64.zip     portable build; this is what the in-app updater downloads
    Buckett-Setup-<version>.exe       per-user installer (skipped with -NoInstaller)

.PARAMETER Runtime
  Runtime identifier to publish for. Defaults to win-x64.

.PARAMETER SelfContained
  Bundle the .NET runtime (default). Pass -SelfContained:$false for a smaller
  framework-dependent build that requires the .NET 8 Desktop Runtime.

.PARAMETER NoInstaller
  Skip the installer and produce only the portable ZIP. Useful on a machine
  without Inno Setup.
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [bool] $SelfContained = $true,
    [switch] $NoInstaller
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot          # windows/
$repo = Split-Path -Parent $root                  # repository root
$project = Join-Path $root 'src/Buckett/Buckett.csproj'
$versionFile = Join-Path $root 'src/Buckett/Support/AppVersion.cs'

# Single source of truth for the version, mirroring the macOS make_app.sh.
$match = Select-String -Path $versionFile -Pattern 'Marketing\s*=\s*"([^"]+)"'
if (-not $match) { throw "Could not extract the version from $versionFile" }
$version = $match.Matches[0].Groups[1].Value
Write-Host "Building Buckett $version for $Runtime"

$dist = Join-Path $root 'dist'
$appDir = Join-Path $dist 'Buckett'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $($SelfContained.ToString().ToLower()) `
    --output $appDir `
    -p:Version=$version `
    -p:AssemblyVersion=$version.0 `
    -p:FileVersion=$version.0 `
    -p:InformationalVersion=$version `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# Ship the licence next to the binaries so the portable folder stands alone.
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $appDir 'LICENSE.txt') -Force

# --- Portable ZIP -------------------------------------------------------------
# This is the asset the in-app updater looks for on GitHub Releases.
$zip = Join-Path $dist "Buckett-$version-$Runtime.zip"
Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Built: $zip ($zipSize MB)"

# --- Installer ----------------------------------------------------------------
if ($NoInstaller) {
    Write-Host 'Skipping the installer (-NoInstaller).'
    return
}

function Find-InnoSetup {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }
    return $null
}

$iscc = Find-InnoSetup
if (-not $iscc) {
    throw 'Inno Setup (ISCC.exe) was not found. Install it (choco install innosetup) ' +
          'or re-run with -NoInstaller to build only the portable ZIP.'
}

$script = Join-Path $PSScriptRoot 'installer.iss'
& $iscc "/DAppVersion=$version" $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

$installer = Join-Path $dist "Buckett-Setup-$version.exe"
if (-not (Test-Path $installer)) { throw "Inno Setup did not produce $installer" }
$installerSize = [math]::Round((Get-Item $installer).Length / 1MB, 1)
Write-Host "Built: $installer ($installerSize MB)"
Write-Host "App folder: $appDir"
