# Harmony patch compatibility

Harmony ID: `com.lucaswilluweit.tarkovperformancesuite.timing`

The suite installs only prefix/postfix timing patches and only when `Diagnostics.MethodTimingEnabled=true`. Each target is resolved with an exact parameter and return signature. Missing or mismatched targets are skipped. Prefix/postfix errors fail open and a three-error circuit breaker disables timing while originals continue.

| Target | Suite behavior | Owners already present |
|---|---|---|
| `EFT.PlayerBody.UpdatePlayerRenders(EPointOfView,EPlayerSide)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `EFT.PlayerBody.IsVisible()` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `FikaClient.Update()` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |
| `ObservedPlayer.ManualStateUpdate(double)` | read timestamp before/after | Cannot be proven statically; inspected at runtime before patch |

The current production reference installation was not launched or modified, and no disposable test client exists. Therefore claiming an owner list now would be fabrication. Before patching, the plugin calls `Harmony.GetPatchInfo`, logs all owners, and writes them to the F10 diagnostic report. Run that report in the disposable installation to complete this table with live values.

The shadow experiment uses no Harmony patches. It changes only `Renderer.shadowCastingMode` on verified remote-AI `PlayerBody` descendants and restores cached values.

