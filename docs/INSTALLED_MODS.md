# Relevant installed mods

Versions below come from the current BepInEx load log, which is more reliable than DLL file versions for several mods.

| Mod | Version | Suite relationship |
|---|---:|---|
| SPT.Core | 4.0.13 | Required runtime; detected, not patched |
| Fika.Core | 2.3.3 | Optional; detected dynamically; diagnostics only |
| SAIN | 4.4.2 | Detected; decision/vision/combat code not touched |
| BigBrain | 1.4.0 | Detected; not touched |
| ORBIT | 1.0.0 | Detected; not touched |
| acidphantasm-botplacementsystem (ABPS) | 2.0.13 | Detected; spawning not touched |
| MoreBotsAPI | 2.0.1 | Detected; registrations and custom roles not touched |
| BlackDiv | 1.1.1 | Detected; custom roles not touched |
| RUAFComeHome | 1.1.1 | Detected; custom roles not touched |
| TacticalToaster-UNTARGH | 3.1.0 | Detected; custom roles not touched |
| DrakiaXYZ-Waypoints | 1.8.2 | Detected; navigation not touched |
| Unity Toolkit | 2.0.1 | Detected; PlayerLoop not touched |
| Looting Bots | Not detected by name | Optional; not touched |
| Questing Bots | Not detected by name | Optional; not touched |
| Simple Declutter | Not detected by name | Optional; not touched |

The preloader log also shows custom-role patchers from BlackDiv, MoreBotsAPI, RUAF, UNTAR, SPT, and Unity Toolkit. This suite does not modify `WildSpawnType` or install a prepatcher.

