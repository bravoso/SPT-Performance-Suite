# Candidate hot paths

Inspection used ILSpyCmd 9.1 against the installed DLLs. Timing is diagnostics-only, disabled by default, and never skips or changes an original method. Version 0.1.1 adds verified high-level `GameWorld` and `Player` update/presentation targets so a CPU-bound frame can be narrowed before considering any optimization.

Installed assembly fingerprints:

- `Assembly-CSharp.dll` SHA-256: `FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982`
- `Fika.Core.dll` SHA-256: `0FDF5A22E6BA801B9C4704D3E1766F321CB29A26B30D858CB11D5E0D8B594746`

| Assembly | Type and method | Access / virtual | Signature fingerprint | Why selected | Effect domain |
|---|---|---|---|---|---|
| Assembly-CSharp | `EFT.PlayerBody.UpdatePlayerRenders(EPointOfView, EPlayerSide): void` | public / non-virtual | `33881f5ef18ed7553ece6584dd3483f0704268fbfd53c7f4f6f7e23380ce1f40` | verified presentation renderer update | presentation only |
| Assembly-CSharp | `EFT.PlayerBody.IsVisible(): bool` | public / non-virtual | `0b4795b7512df469493d5878b4d22d6628357c40fbe681bd3d888d0b875b4d06` | verified player-body visibility query | presentation/culling observation only |
| Fika.Core 2.3.3 | `Fika.Core.Networking.FikaClient.Update(): void` | private / non-virtual | `3c5ae346908b561729d48842ed0341a6d8f9ca68e953f4d6b4c77ecfdce0ffa5` | verified loop calls `ObservedPlayers[i].ManualStateUpdate(networkTime)` | network/state application; measure only |
| Fika.Core 2.3.3 | `Fika.Core.Main.Players.ObservedPlayer.ManualStateUpdate(double): void` | public / non-virtual | `b80312792a915ddd0d919ee5bc7df86681d5f5017f47357e14c743dab7eb5f07` | verified per-observed-player state path | network/presentation; measure only |

The signature fingerprint is SHA-256 of the fully qualified signature. The plugin also hashes each installed method body's IL bytes at runtime and includes that value in the Num9 report. That runtime value is preferable because it is taken from the post-prepatch assembly actually loaded by Unity.

AI decision logic, animation setters, procedural weapon internals, physics, audio, inventory, health, spawning, and combat paths remain unpatched until benchmark evidence justifies a separate diagnostic target. `FBBIKUpdate` is timed only as a high-level presentation bucket; no IK behavior is changed.

Additional 0.1.1 timing-only targets: `GameWorld.Update`, `GameWorld.LateUpdateWorld`, `Player.UpdateTick`, `Player.FixedUpdateTick`, `Player.LateUpdate`, `Player.VisualPass`, `Player.ComplexUpdate`, `Player.ComplexLateUpdate`, `Player.ArmsUpdate`, `Player.BodyUpdate`, `Player.ManualUpdate`, `Player.FBBIKUpdate`, and Fika's override of `FikaPlayer.ManualUpdate`. Exact signatures, IL hashes, and owners are written at runtime before patching.

Version 0.2.0 corrects a Fika-specific blind spot. Installed Fika 2.3.3 overrides `ObservedPlayer.LateUpdate()` and `ObservedPlayer.ManualUpdate(float,float?,int)`, so timing only the EFT base methods does not measure remote observed AI. The exact override plus private `ObservedVisualPass(float,int)` and `ObservedFBBIKUpdate(float,int)` are now timing-only targets. `EFT.Player.PropUpdate()` and `BasePhysicalClass.LateUpdate()` are also timed because the installed observed-player override calls them. These measurements are nested and must not be summed.

Installed Fika code confirms that the playing client runs `FikaClient.Update()`, polls network events, and calls `ManualStateUpdate(networkTime)` for each observed player. Its `ObservedPlayer.LateUpdate()` performs client representation work including physical late update, visibility-gated visual/IK work, prop transforms, and corpse-culling maintenance. SAIN/BigBrain authority remains on the raid host and is not patched by this client suite.
