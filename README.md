# Tarkov Performance Suite

Profiler-first BepInEx diagnostics and conservative performance experiments for SPT/EFT. Version 0.2.0 targets the inspected EFT 0.16.9.40087 / SPT 4.0.13 / Fika 2.3.3 environment, but all game references are supplied at build time rather than committed as binaries.

The suite does not change AI decisions, bot population, damage, networking, inventory, navigation, or global quality settings. Its behavior-changing experiments are adaptive distant remote-AI shadows and an offscreen remote-AI skinned-mesh guard. Both are independently configurable, disabled by default, restricted to confirmed AI, and restore original renderer state.

## Build

Use a reference installation only for assemblies:

```powershell
$env:SPT_REFERENCE_ROOT = 'D:\path\to\reference-SPT'
.\scripts\build.ps1
```

Deployment is allowed only to a different disposable installation:

```powershell
$env:SPT_TEST_ROOT = 'D:\path\to\disposable-SPT'
.\scripts\deploy-test.ps1
```

See `docs/IN_GAME_TESTING.md` before enabling the experiment.
