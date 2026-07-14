param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [string]$TestRoot = $env:SPT_TEST_ROOT,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$ForceConfig
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot -TestRoot $TestRoot -RequireTestRoot
& (Join-Path $PSScriptRoot 'build.ps1') -ReferenceRoot $paths.ReferenceRoot -Configuration $Configuration

$source = Join-Path $repo "src\TarkovPerformance.Plugin\bin\$Configuration\netstandard2.1"
$coreSource = Join-Path $repo "src\TarkovPerformance.Core\bin\$Configuration\netstandard2.1"
$destination = Join-Path $paths.TestRoot 'BepInEx\plugins\TarkovPerformanceSuite'
$configDestination = Join-Path $paths.TestRoot 'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.cfg'
New-Item -ItemType Directory -Force -Path $destination | Out-Null

foreach ($file in @('TarkovPerformanceSuite.dll','TarkovPerformanceSuite.pdb')) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination (Join-Path $destination $file) -Force
}
foreach ($file in @('TarkovPerformance.Core.dll','TarkovPerformance.Core.pdb')) {
    Copy-Item -LiteralPath (Join-Path $coreSource $file) -Destination (Join-Path $destination $file) -Force
}

if ($ForceConfig -or -not (Test-Path -LiteralPath $configDestination)) {
    Copy-Item -LiteralPath (Join-Path $repo 'config\TarkovPerformanceSuite.cfg') -Destination $configDestination -Force
}

Write-Host "Deployed only Tarkov Performance Suite files to: $destination"

