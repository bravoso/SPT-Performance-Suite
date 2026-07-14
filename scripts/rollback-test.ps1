param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [string]$TestRoot = $env:SPT_TEST_ROOT,
    [switch]$RemoveConfig
)

$ErrorActionPreference = 'Stop'
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot -TestRoot $TestRoot -RequireTestRoot
$destination = Join-Path $paths.TestRoot 'BepInEx\plugins\TarkovPerformanceSuite'
$knownFiles = @(
    'TarkovPerformanceSuite.dll', 'TarkovPerformanceSuite.pdb',
    'TarkovPerformance.Core.dll', 'TarkovPerformance.Core.pdb'
)
foreach ($file in $knownFiles) {
    $target = Join-Path $destination $file
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}

if ($RemoveConfig) {
    $config = Join-Path $paths.TestRoot 'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.cfg'
    if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config -Force }
}

if (Test-Path -LiteralPath $destination) {
    $remaining = Get-ChildItem -LiteralPath $destination -Force
    if ($remaining.Count -eq 0) { Remove-Item -LiteralPath $destination -Force }
}
Write-Host 'Rollback complete. No unrelated files were removed.'

