# Tarkov Performance Suite

Profiler-first BepInEx diagnostics and reversible performance experiments for SPT/EFT. Version 0.8.0 targets the inspected EFT 0.16.9.40087 / SPT 4.0.13 / Fika 2.3.3 environment, but all game references are supplied at build time rather than committed as binaries.

The suite does not change AI decisions, bot population, damage, networking, inventory, or navigation. Version 0.8.0 adds a client-only combat-presentation budget for distant casing physics and active-bullet flyby-audio scans, while deeper capture-only timing now covers EFT's real world-tick stages, culling, effects, shells, and job scheduler. PiP scopes retain vanilla HDR and unlimited vanilla refresh; only normal-optic render resolution and MSAA are eligible for reduction. Num4 restores all reversible state for a true mid-raid A/B test. It does not force LODs or change global LOD bias.

The optional aggressive profile can still trade texture, shadow, light, particle, and skin-weight quality for headroom, but it no longer changes LOD selection. `TextureMipLimit` should be selected before loading a raid because changing it forces Unity to re-upload affected textures. The declutter classifier is renderer-only: it never destroys objects or colliders, and its scan is amortized after one discovery pass.

Use F12 and select `OldCpuAggressive` under **Quick Setup** for a 4-core/8-thread client. Num4 toggles every optimization together for an A/B test, Num7 toggles the compact overlay, Num8 toggles continuous back-to-back profiling, and Num9 exports an immediate report. Every CSV sample records whether the A/B master was on or off.

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
