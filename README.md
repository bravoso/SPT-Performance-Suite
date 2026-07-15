# Tarkov Performance Suite 1.0

Tarkov Performance Suite reduces CPU work that the client does not need every rendered frame. It focuses on hidden or distant character presentation, repeated light and world-visual maintenance, combat effects that cannot be seen, known mod scan/log hot paths, and loading preparation. Damage, AI decisions, network snapshots, input, sound, and nearby or visible combat remain authoritative.

The 1.0 production package starts silently: the Extreme general-purpose preset and all optimization modules are enabled, while the diagnostics overlay, bot-counter HUD, method profiler, and loading/server reports are disabled. F12 exposes every setting. Changes to gameplay settings are saved immediately and applied in the running game; choosing a preset applies and saves its complete values. Loading and server settings are read at process startup and therefore require a restart.

## Presets

- `Balanced`: lighter visual and update budgets.
- `Performance`: the full optimization stack with moderate distances and rates.
- `Extreme`: strongest production settings for every map; selected by default.
- `Custom`: selected automatically when an individual optimization value is edited.

Extreme keeps the texture mip limit at zero and leaves LOD selection untouched. It deliberately reduces shadow distance/resolution, transient pixel lights, particle raycasts, distant presentation frequency, and repeated ambient/world command rebuilding.

## Measured result

Raid-to-raid conditions are not identical, so results are not a guarantee. In the conservative entity-matched Customs comparison, the latest build improved average FPS by 14.9%, 5% low by 30.8%, and 1% low by 10.5%; median main-thread time fell 10.9% and its 95th percentile fell 23.2%. The raw earliest-to-latest Customs captures measured +26.0% average FPS, +35.0% 5% low, and +27.8% 1% low. One same-raid Streets A/B observation rose from roughly 85 FPS disabled to 100-105 enabled. Hardware, bot count, weather, combat, other mods, and route strongly affect the outcome.

## Install

1. Close EFT and SPT.
2. Extract the release ZIP into the SPT game folder, keeping its `BepInEx` and `SPT` folders.
3. If the SPT server is on another computer, install `SPT\user\mods\TarkovPerformanceLoadingServer` on that actual server instead.
4. With Fika, install the `BepInEx` portion on each playable client and headless client. Install the server portion once on the real SPT server.

The SPT/BepInEx installation is required. Fika, Dynamic Maps, SAIN, ORBIT, and BigBrain are optional. The suite discovers them at runtime and skips their integration when absent; it does not link against their assemblies. The client works in ordinary non-Fika SPT, and the server accelerator is optional.

The package includes the unmodified MIT-licensed PiP-Disabler 1.5.0 by Fiodorwellfme. If it is removed, the suite still loads and uses vanilla PiP instead of the no-PiP replacement.

## Controls

- `Num4`: all gameplay optimizations on/off for an A/B comparison.
- `Num3`: no-PiP main-camera scope replacement/full-resolution vanilla PiP.
- `Num7`: diagnostics overlay.
- `Num8`: repeating 120-second captures.
- `Num9`: immediate diagnostic report.
- `F12`: Configuration Manager settings, including the optional bot counter and profiler.

Profiling output is written only after profiling is explicitly enabled. See [SPT Forge description](docs/SPT_FORGE_DESCRIPTION.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [benchmark protocol](docs/BENCHMARK_PROTOCOL.md).

## Build

```powershell
$env:SPT_REFERENCE_ROOT = 'D:\path\to\reference-SPT'
.\scripts\build.ps1
.\scripts\package-friend.ps1 -ReferenceRoot $env:SPT_REFERENCE_ROOT
```

Game and SPT assemblies are build-time references and are not committed or redistributed.
