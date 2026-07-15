param(
    [string]$ServerRoot = $env:SPT_SERVER_ROOT,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ServerRoot)) { throw 'Set SPT_SERVER_ROOT or pass -ServerRoot.' }
$serverRootResolved = (Resolve-Path -LiteralPath $ServerRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $serverRootResolved 'SPTarkov.Server.Core.dll'))) {
    throw "SPTarkov.Server.Core.dll was not found under: $serverRootResolved"
}
$dotnet = Join-Path $repo '.tools\dotnet9\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The .NET 9 SDK is missing from .tools\dotnet9.' }
$env:DOTNET_CLI_HOME = Join-Path $repo '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location (Join-Path $repo 'src\TarkovPerformance.Server')
try {
    & $dotnet build '.\TarkovPerformance.Server.csproj' -c $Configuration "/p:SptServerRoot=$serverRootResolved"
    if ($LASTEXITCODE -ne 0) { throw "Server build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
