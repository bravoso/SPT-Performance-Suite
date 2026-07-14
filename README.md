# Tarkov Performance Suite

Profiler-first BepInEx diagnostics and reversible performance experiments for SPT/EFT. Version 0.4.0 targets the inspected EFT 0.16.9.40087 / SPT 4.0.13 / Fika 2.3.3 environment, but all game references are supplied at build time rather than committed as binaries.

The suite does not change AI decisions, bot population, damage, networking, inventory, or navigation. Version 0.4.0 adds a friend-ready old-CPU preset that uses EFT/Fika's existing visibility result to budget only hidden remote-character presentation, removes a measured periodic full-scene search from a compatible bot-counter mod, and smooths background/upload/physics allocation work. The preset does not change LOD or texture quality.

The aggressive profile intentionally trades visual quality for lower scene, render-thread, and memory pressure. `TextureMipLimit` should be selected before loading a raid because changing it forces Unity to re-upload affected textures. The declutter classifier is deliberately renderer-only: it never destroys objects or colliders, and its scan is amortized after one discovery pass.

Use F12 and select `OldCpuAggressive` under **Quick Setup** for a 4-core/8-thread client. Num7 toggles the compact overlay, Num8 records a benchmark, and Num9 exports a report.

## Build

Use a reference installation only for assemblies:

```powershell
$env:SPT_REFERENCE_ROOT = 'D:\path\to\reference-SPT'
.\scripts\build.ps1
```

Deployment to a test installation:

```powershell
$env:SPT_TEST_ROOT = 'D:\path\to\disposable-SPT'
.\scripts\deploy-test.ps1
```

See `docs/IN_GAME_TESTING.md` before enabling the experiment.

Create a sendable friend package with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-friend.ps1 -ReferenceRoot 'D:\SPT'
```
