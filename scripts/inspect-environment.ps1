param([string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT)

$ErrorActionPreference = 'Stop'
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot
$root = $paths.ReferenceRoot
$log = Join-Path $root 'BepInEx\LogOutput.log'
$exe = Get-Item -LiteralPath (Join-Path $root 'EscapeFromTarkov.exe')
$managed = Join-Path $root 'EscapeFromTarkov_Data\Managed'

[PSCustomObject]@{
    ReferenceRoot = $root
    EftFileVersion = $exe.VersionInfo.FileVersion
    BepInExLog = $log
    ManagedAssemblyCount = (Get-ChildItem -LiteralPath $managed -Filter '*.dll' -File).Count
    PluginAssemblyCount = (Get-ChildItem -LiteralPath (Join-Path $root 'BepInEx\plugins') -Filter '*.dll' -File -Recurse).Count
    PatcherAssemblyCount = (Get-ChildItem -LiteralPath (Join-Path $root 'BepInEx\patchers') -Filter '*.dll' -File -Recurse).Count
}

if (Test-Path -LiteralPath $log) {
    Get-Content -LiteralPath $log | Where-Object { $_ -match 'BepInEx 5|Running under Unity|Loading \[SPT.Core|Loading \[Fika.Core' } | Select-Object -Unique
}

