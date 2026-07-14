param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot
$localDotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

& $dotnet build (Join-Path $repo 'TarkovPerformanceSuite.sln') -c $Configuration "/p:SptReferenceRoot=$($paths.ReferenceRoot)"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

& $dotnet run --project (Join-Path $repo 'tests\TarkovPerformance.Tests\TarkovPerformance.Tests.csproj') -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }

