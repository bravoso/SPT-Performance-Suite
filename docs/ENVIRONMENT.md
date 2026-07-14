# Inspected environment

Inspection date: 2026-07-13. The path below is deliberately not committed; `D:\SPT` was used read-only through `SPT_REFERENCE_ROOT` during this run.

| Item | Detected value | Source |
|---|---:|---|
| EFT executable | 0.16.9.40087 | `EscapeFromTarkov.exe` file metadata |
| Unity | 2022.3.43f1 | version string in `EscapeFromTarkov_Data/globalgamemanagers` |
| SPT | 4.0.13 | BepInEx startup log and `spt-reflection.dll` |
| BepInEx | 5.4.23.2 | startup log and assembly metadata |
| CLR | 4.0.30319.42000 | BepInEx startup log |
| Fika | 2.3.3 | startup log and `Fika.Core.dll` metadata |
| HarmonyX used by BepInEx | 2.9.0 | `BepInEx/core/0Harmony.dll` |

Unity's version was not inferred from the misleading `Running under Unity v0.16.9.4008` log line; it was verified in the Unity data file.

No separate `SPT_TEST_ROOT` was found. The scripts therefore refuse deployment in this run. A disposable copy must be configured before in-game testing.

The installed client contains 168 managed DLLs, 12 BepInEx core DLLs, 90 plugin DLLs, and 7 patcher DLLs. No production files were changed.

