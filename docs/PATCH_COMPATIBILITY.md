# Harmony patch compatibility

Harmony ID: `com.lucaswilluweit.tarkovperformancesuite.timing`

The suite installs only prefix/postfix timing patches and only when `Diagnostics.MethodTimingEnabled=true`. By default they record only while a benchmark capture is active. Each target is resolved with an exact parameter and return signature. Missing or mismatched targets are skipped. Prefix/postfix errors fail open and a three-error circuit breaker disables timing while originals continue. Version 0.2.0 includes the exact Fika observed-player overrides and nested presentation methods listed in `CANDIDATE_HOT_PATHS.md`.

| Target | Suite behavior | Owners already present |
|---|---|---|
| `EFT.PlayerBody.UpdatePlayerRenders(EPointOfView,EPlayerSide)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `EFT.PlayerBody.IsVisible()` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `FikaClient.Update()` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.ManualStateUpdate(double)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.LateUpdate()` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.ManualUpdate(float,float?,int)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.ObservedVisualPass(float,int)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.ObservedFBBIKUpdate(float,int)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |

Before patching, the plugin calls `Harmony.GetPatchInfo`, logs all owners, and writes them to the Num9 diagnostic report. The Reserve run found `com.vai.spmc` on EFT's base `Player.LateUpdate`; all other 0.1.1 targets reported no existing owner. Version 0.2.0's new exact Fika targets must still be verified by the next runtime report.

The shadow and offscreen-skinning experiments use no Harmony patches. They change only `Renderer.shadowCastingMode` or `SkinnedMeshRenderer.updateWhenOffscreen` on verified remote-AI `PlayerBody` descendants and restore cached values.
