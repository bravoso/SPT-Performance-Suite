param(
    [string]$ReferenceRoot = $env:SPT_REFERENCE_ROOT,
    [string]$SourceRoot,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Join-Path $repo '.tools\PiP-Disabler' }
if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot 'PiPDisabler.csproj'))) {
    throw "PiP-Disabler 1.5.0 source is required at $SourceRoot (official source: https://github.com/Fiodorwellfme/PiP-Disabler)."
}

$paths = & (Join-Path $PSScriptRoot 'Resolve-SptPaths.ps1') -ReferenceRoot $ReferenceRoot
$localDotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$work = Join-Path $repo 'artifacts\.pip-disabler-build'
$output = Join-Path $repo 'artifacts\PiP-Disabler-1.5.0'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work, $output | Out-Null
Copy-Item -Path (Join-Path $SourceRoot '*') -Destination $work -Recurse -Force

$projectPath = Join-Path $work 'PiPDisabler.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
foreach ($reference in $project.Project.ItemGroup.Reference) {
    if ($reference.HintPath -and $reference.HintPath.StartsWith('..\..\')) {
        $relative = $reference.HintPath.Substring(6)
        $hintNode = $reference.SelectSingleNode('HintPath')
        $hintNode.InnerText = if ($reference.Include -eq 'Newtonsoft.Json') {
            [string](Join-Path ([string]$paths.ReferenceRoot) 'EscapeFromTarkov_Data\Managed\Newtonsoft.Json.dll')
        } else {
            [string](Join-Path ([string]$paths.ReferenceRoot) $relative)
        }
    }
}
foreach ($group in @($project.Project.ItemGroup)) {
    foreach ($package in @($group.PackageReference)) {
        if ($package.Include -eq 'Newtonsoft.Json') { [void]$group.RemoveChild($package) }
    }
}
foreach ($target in @($project.Project.Target)) {
    if ($target.Name -eq 'PostBuild') { [void]$project.Project.RemoveChild($target) }
}
$project.Save($projectPath)

& $dotnet build $projectPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "PiP-Disabler build failed with exit code $LASTEXITCODE." }
$binary = Join-Path $work "bin\$Configuration\net472\PiP-Disabler.dll"
Copy-Item -LiteralPath $binary -Destination $output
Copy-Item -LiteralPath (Join-Path $work 'Resources\Shaders\pipdisabler_reticle_shaders.bundle') -Destination $output
Copy-Item -LiteralPath (Join-Path $work 'Resources\Shaders\pipdisabler_effect_shaders.bundle') -Destination $output
Copy-Item -LiteralPath (Join-Path $work 'custom_mesh_surgery_settings.json') -Destination $output
Copy-Item -LiteralPath (Join-Path $work 'LICENSE') -Destination (Join-Path $output 'LICENSE-PiP-Disabler.txt')
Write-Host "PiP replacement output: $output"
