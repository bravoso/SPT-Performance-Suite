param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [string]$TestRoot = $env:SPT_TEST_ROOT,
    [switch]$RequireTestRoot
)

$ErrorActionPreference = 'Stop'

function Resolve-SptRoot([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Label is not set. Pass the matching parameter or environment variable."
    }

    $resolved = (Resolve-Path -LiteralPath $Value).Path.TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'EscapeFromTarkov.exe'))) {
        throw "$Label does not contain EscapeFromTarkov.exe: $resolved"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $resolved 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))) {
        throw "$Label does not contain Assembly-CSharp.dll: $resolved"
    }
    return $resolved
}

$reference = Resolve-SptRoot $ReferenceRoot 'SPT_REFERENCE_ROOT'
$test = $null
if ($RequireTestRoot -or -not [string]::IsNullOrWhiteSpace($TestRoot)) {
    $test = Resolve-SptRoot $TestRoot 'SPT_TEST_ROOT'
    if ([StringComparer]::OrdinalIgnoreCase.Equals($reference, $test)) {
        throw 'SPT_TEST_ROOT must be a separate disposable installation, not SPT_REFERENCE_ROOT.'
    }
}

[PSCustomObject]@{ ReferenceRoot = $reference; TestRoot = $test }

