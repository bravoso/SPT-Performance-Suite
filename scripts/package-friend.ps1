param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot
& (Join-Path $PSScriptRoot 'build.ps1') -ReferenceRoot $paths.ReferenceRoot -Configuration $Configuration
& (Join-Path $PSScriptRoot 'build-server.ps1') -ServerRoot (Join-Path $paths.ReferenceRoot 'SPT') -Configuration $Configuration
& (Join-Path $PSScriptRoot 'build-pip-disabler.ps1') -ReferenceRoot $paths.ReferenceRoot -Configuration $Configuration

$version = '1.0.1'
$stage = Join-Path $repo "artifacts\TarkovPerformanceSuite-$version"
$zip = "$stage.zip"
$pluginDestination = Join-Path $stage 'BepInEx\plugins\TarkovPerformanceSuite'
$configDestination = Join-Path $stage 'BepInEx\config'
$serverDestination = Join-Path $stage 'SPT\user\mods\TarkovPerformanceLoadingServer'
$pipDestination = Join-Path $stage 'BepInEx\plugins\PiP-Disabler'

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Force -Path $pluginDestination, $configDestination, $serverDestination, $pipDestination | Out-Null

$pluginSource = Join-Path $repo "src\TarkovPerformance.Plugin\bin\$Configuration\netstandard2.1"
$coreSource = Join-Path $repo "src\TarkovPerformance.Core\bin\$Configuration\netstandard2.1"
Copy-Item -LiteralPath (Join-Path $pluginSource 'TarkovPerformanceSuite.dll') -Destination $pluginDestination
Copy-Item -LiteralPath (Join-Path $coreSource 'TarkovPerformance.Core.dll') -Destination $pluginDestination
$loadingSource = Join-Path $repo "src\TarkovPerformance.Loading\bin\$Configuration\netstandard2.1"
Copy-Item -LiteralPath (Join-Path $loadingSource 'TarkovPerformance.Loading.dll') -Destination $pluginDestination
Copy-Item -LiteralPath (Join-Path $repo 'config\TarkovPerformanceSuite.cfg') -Destination (Join-Path $configDestination 'com.lucaswilluweit.tarkovperformancesuite.cfg')
Copy-Item -LiteralPath (Join-Path $repo 'config\TarkovPerformance.Loading.cfg') -Destination (Join-Path $configDestination 'com.lucaswilluweit.tarkovperformancesuite.loading.cfg')
Copy-Item -LiteralPath (Join-Path $repo 'config\com.fiodor.pipdisabler.cfg') -Destination $configDestination
Copy-Item -Path (Join-Path $repo 'artifacts\PiP-Disabler-1.5.0\*') -Destination $pipDestination -Recurse
Copy-Item -LiteralPath (Join-Path $repo "src\TarkovPerformance.Server\bin\$Configuration\TarkovPerformanceLoadingServer\TarkovPerformance.LoadingServer.dll") -Destination $serverDestination
Copy-Item -LiteralPath (Join-Path $repo 'src\TarkovPerformance.Server\config.json') -Destination $serverDestination
Copy-Item -LiteralPath (Join-Path $repo 'docs\FRIEND_SETUP.txt') -Destination (Join-Path $stage 'READ ME - INSTALL.txt')
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $stage

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Production package: $zip"
