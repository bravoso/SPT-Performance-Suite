# Assembly references

All references resolve from `SPT_REFERENCE_ROOT`; no installed game DLL is copied into Git.

| Assembly/group | Requirement | Use |
|---|---|---|
| `BepInEx/core/BepInEx.dll` | Required | Plugin, logging, configuration |
| `BepInEx/core/0Harmony.dll` | Required | Optional diagnostics-only timing patches |
| `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` | Required | Verified `EFT.Player`, `EFT.GameWorld`, and presentation signatures |
| `Comfort.dll`, `Comfort.Unity.dll` | Required | Game singleton/lifecycle access where verified |
| Unity Core, IMGUI, Input, Animation, Physics, Audio, Particle, Profiler modules | Required at build time | Overlay, counters, entity presentation and renderer state |
| `spt-reflection.dll` | Detected, not referenced | Inspected to understand SPT conventions; no hard dependency |
| `Fika.Core.dll` | Optional, not referenced | Located and accessed by cached reflection only when loaded |
| SAIN, BigBrain, ORBIT, ABPS, MoreBotsAPI and faction DLLs | Not referenced | Not touched |

The installed client is Unity Mono on CLR 4.x. Installed plugins do not consistently retain a target-framework attribute, so the exact original Fika TFM could not be proven from metadata. The suite targets `netstandard2.1`, which compiles successfully against the installed Unity 2022.3/BepInEx assemblies. This is recorded as an assumption pending a disposable in-game load test.

