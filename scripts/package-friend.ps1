param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot
& (Join-Path $PSScriptRoot 'build.ps1') -ReferenceRoot $paths.ReferenceRoot -Configuration $Configuration

$version = '0.8.0'
$stage = Join-Path $repo "artifacts\TarkovPerformanceSuite-$version-OldCPU"
$zip = "$stage.zip"
$pluginDestination = Join-Path $stage 'BepInEx\plugins\TarkovPerformanceSuite'
$configDestination = Join-Path $stage 'BepInEx\config'

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Force -Path $pluginDestination, $configDestination | Out-Null

$pluginSource = Join-Path $repo "src\TarkovPerformance.Plugin\bin\$Configuration\netstandard2.1"
$coreSource = Join-Path $repo "src\TarkovPerformance.Core\bin\$Configuration\netstandard2.1"
Copy-Item -LiteralPath (Join-Path $pluginSource 'TarkovPerformanceSuite.dll') -Destination $pluginDestination
Copy-Item -LiteralPath (Join-Path $coreSource 'TarkovPerformance.Core.dll') -Destination $pluginDestination
Copy-Item -LiteralPath (Join-Path $repo 'config\TarkovPerformanceSuite.OldCPU.cfg') -Destination (Join-Path $configDestination 'com.lucaswilluweit.tarkovperformancesuite.cfg')
Copy-Item -LiteralPath (Join-Path $repo 'docs\FRIEND_SETUP.txt') -Destination (Join-Path $stage 'READ ME - INSTALL.txt')

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Friend package: $zip"
