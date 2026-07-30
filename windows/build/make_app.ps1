<#
.SYNOPSIS
  Builds Buckett for Windows and packages it as a portable ZIP.

.DESCRIPTION
  Produces windows/dist/Buckett/ (a self-contained x64 build that needs no
  .NET install) and windows/dist/Buckett-<version>-win-x64.zip, which is the
  asset the in-app updater downloads from GitHub Releases.

.PARAMETER Runtime
  Runtime identifier to publish for. Defaults to win-x64.

.PARAMETER SelfContained
  Bundle the .NET runtime (default). Pass -SelfContained:$false for a smaller
  framework-dependent build that requires the .NET 8 Desktop Runtime.
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [bool] $SelfContained = $true
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

$zip = Join-Path $dist "Buckett-$version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Built: $zip ($size MB)"
Write-Host "App folder: $appDir"
