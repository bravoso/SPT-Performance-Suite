param(
    [string]$PackageRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\TarkovPerformanceSuite-1.0.1'),
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageRoot).Path
$repo = Split-Path -Parent $PSScriptRoot
$required = @(
    'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformanceSuite.dll',
    'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformance.Core.dll',
    'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformance.Loading.dll',
    'BepInEx\plugins\PiP-Disabler\PiP-Disabler.dll',
    'BepInEx\plugins\PiP-Disabler\LICENSE-PiP-Disabler.txt',
    'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.cfg',
    'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.loading.cfg',
    'SPT\user\mods\TarkovPerformanceLoadingServer\TarkovPerformance.LoadingServer.dll',
    'SPT\user\mods\TarkovPerformanceLoadingServer\config.json',
    'READ ME - INSTALL.txt',
    'THIRD_PARTY_NOTICES.md',
    'LICENSE'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $relative))) { throw "Release file is missing: $relative" }
}

$clientConfig = Get-Content -Raw -LiteralPath (Join-Path $package 'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.cfg')
foreach ($requiredSetting in @(
    'PerformancePreset = Extreme',
    'AllOptimizationsEnabled = true',
    'OverlayEnabled = false',
    'MethodTimingEnabled = false',
    'SoundOnlyRemoteShotsOnFikaClients = false'
)) {
    if (-not $clientConfig.Contains($requiredSetting)) { throw "Production client setting is missing: $requiredSetting" }
}
if ($clientConfig -notmatch '(?ms)\[HUD - Bot Counter\].*?Enabled = false') { throw 'Bot-counter HUD must be disabled by default.' }
if ($clientConfig -notmatch '(?ms)\[CPU - Remote Characters\].*?Enabled = false') { throw 'Unsafe observed-player update suppression must remain retired.' }
if ($clientConfig -notmatch '(?ms)\[Headless Authority\].*?Enabled = false') { throw 'Experimental headless authority pacing must be disabled by default.' }

$loadingConfig = Get-Content -Raw -LiteralPath (Join-Path $package 'BepInEx\config\com.lucaswilluweit.tarkovperformancesuite.loading.cfg')
if (-not $loadingConfig.Contains('Write loading reports = false')) { throw 'Loading reports must be disabled by default.' }
$serverConfig = Get-Content -Raw -LiteralPath (Join-Path $package 'SPT\user\mods\TarkovPerformanceLoadingServer\config.json')
if ($serverConfig -notmatch '"writeReports"\s*:\s*false') { throw 'Server reports must be disabled by default.' }

if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) { throw 'Set SPT_REFERENCE_ROOT or pass -ReferenceRoot for assembly verification.' }
$cecil = Join-Path $ReferenceRoot 'BepInEx\core\Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecil)) { throw "Mono.Cecil.dll was not found: $cecil" }
Add-Type -Path $cecil
$forbidden = @('Fika.Core', 'DynamicMaps', 'SAIN', 'ORBIT', 'DrakiaXYZ-BigBrain')
foreach ($relative in @(
    'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformanceSuite.dll',
    'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformance.Loading.dll'
)) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $package $relative))
    try {
        $references = @($assembly.MainModule.AssemblyReferences | ForEach-Object Name)
        foreach ($name in $forbidden) {
            if ($references -contains $name) { throw "$relative has a hard optional-mod dependency on $name." }
        }
    }
    finally { $assembly.Dispose() }
}

# Combat-safety regression guard: these compatibility tombstones must never regain
# Harmony or gameplay/authority member references. Profiler code may still name these
# methods elsewhere because measuring vanilla execution is safe.
$pluginAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
    (Join-Path $package 'BepInEx\plugins\TarkovPerformanceSuite\TarkovPerformanceSuite.dll')
)
try {
    $retiredTypes = @(
        'TarkovPerformanceSuite.RuntimeFeatures.RemoteUpdateBudgetFeature',
        'TarkovPerformanceSuite.RuntimeFeatures.HeadlessAuthorityFeature'
    )
    $forbiddenMemberPatterns = @(
        'HarmonyLib',
        'EFT\.Player::ArmsUpdate',
        'EFT\.Player::BodyUpdate',
        'EFT\.Player::FBBIKUpdate',
        'EFT\.Player::ComplexLateUpdate',
        'Fika\.Core\.Main\.Components\.BotStateManager',
        'Orbit\.Navigation\.NavJobExecutor'
    )
    foreach ($typeName in $retiredTypes) {
        $type = $pluginAssembly.MainModule.Types | Where-Object FullName -eq $typeName
        if ($null -eq $type) { throw "Combat-safety tombstone is missing: $typeName" }
        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                if ($instruction.Operand -isnot [Mono.Cecil.MemberReference]) { continue }
                $member = [string]$instruction.Operand
                foreach ($pattern in $forbiddenMemberPatterns) {
                    if ($member -match $pattern) {
                        throw "$typeName regained a forbidden combat/authority reference: $member"
                    }
                }
            }
        }
    }
}
finally { $pluginAssembly.Dispose() }

$zip = "$package.zip"
if (-not (Test-Path -LiteralPath $zip)) { throw "Release ZIP is missing: $zip" }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Write-Host "Release verification passed. Optional integrations are soft, production diagnostics are off, retired combat/authority hooks are absent, and every install file is present."
Write-Host "SHA256 $hash"
