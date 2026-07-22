param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = 'dotnet'
}

$env:DOTNET_CLI_HOME = Join-Path $repo '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
if (-not [string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $env:SPT_REFERENCE_ROOT = $ReferenceRoot
}

Push-Location $repo
try {
    & $dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Tool restore failed with exit code $LASTEXITCODE."
    }

    & $dotnet tool run csharpier check src tests
    if ($LASTEXITCODE -ne 0) {
        throw "CSharpier found files that need formatting."
    }

    if (-not [string]::IsNullOrWhiteSpace($ReferenceRoot)) {
        & $dotnet format TarkovPerformanceSuite.sln style --no-restore --verify-no-changes --diagnostics IDE0011 IDE0161 IDE0022 IDE0025 IDE0026 IDE0027 --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw 'The source does not satisfy the configured SPT code-style diagnostics.'
        }
    }
    else {
        Write-Warning 'SPT_REFERENCE_ROOT is not set; reference-dependent Roslyn style checks were skipped.'
    }

    $blockNamespaces = @(rg -l '^namespace [^;]+$' src tests -g '*.cs' -g '!**/bin/**' -g '!**/obj/**')
    if ($blockNamespaces.Count -gt 0) {
        throw "Block-scoped namespaces remain: $($blockNamespaces -join ', ')"
    }

    Write-Host 'Style verification passed.'
}
finally {
    Pop-Location
}
