# Tarkov Performance Suite

Profiler-first BepInEx diagnostics and reversible performance experiments for SPT/EFT. Version 0.3.0 targets the inspected EFT 0.16.9.40087 / SPT 4.0.13 / Fika 2.3.3 environment, but all game references are supplied at build time rather than committed as binaries.

The suite does not change AI decisions, bot population, damage, networking, inventory, or navigation. Conservative experiments cover adaptive distant remote-AI shadows and offscreen skinning. Version 0.3.0 adds opt-in aggressive modules for a global low-cost render/VRAM profile, distant confirmed-remote-AI render LOD, and renderer-only cosmetic decluttering. Every module is independently configurable, disabled by default, and restores the state it changes.

The aggressive profile intentionally trades visual quality for lower scene, render-thread, and memory pressure. `TextureMipLimit` should be selected before loading a raid because changing it forces Unity to re-upload affected textures. The declutter classifier is deliberately renderer-only: it never destroys objects or colliders, and its scan is amortized after one discovery pass.

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
