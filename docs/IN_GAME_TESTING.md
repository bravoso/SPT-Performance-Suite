# Exact in-game test steps

These steps are pending because `SPT_TEST_ROOT` was not found.

1. Make a separate disposable copy of the SPT installation; do not use the primary/reference folder.
2. Set `SPT_REFERENCE_ROOT` to the read-only reference and `SPT_TEST_ROOT` to the disposable copy.
3. Run `scripts/deploy-test.ps1`.
4. Start the disposable server/client and confirm the log shows `Tarkov Performance Suite 0.2.0 loaded` and the detected versions.
5. Confirm `BepInEx/config/com.lucaswilluweit.tarkovperformancesuite.cfg` exists and both behavior-changing experiments are `false`.
6. Enter a raid. Confirm one raid-start log, gradual entity counts, Num7 overlay toggle, and unavailable counters displaying `n/a` without errors.
7. Press Num8, wait for completion, and verify matching CSV/JSON files plus the automatically generated timing report.
8. Press Num9 for an additional on-demand report. Verify runtime profiler values, method timings, and live Harmony owners. F12 remains the Configuration Manager.
9. Enable `RemoteCharacterShadowsDryRun=true` and the shadow experiment. Press Num6 as needed, or use F12. Verify counters and the effective adaptive distance change but renderer shadows do not.
10. Disable shadow dry-run, keep 120 m maximum / 60 m minimum initially, and verify only distant confirmed AI shadows change. Sustained sub-60 FPS should reduce the effective distance in 15 m steps; recovery is deliberately slow.
11. Enable `RemoteAiOffscreenSkinningDryRun=true` and press Num5. Verify only distant, invisible confirmed AI produce candidates. Disable it again before the live test.
12. For the live skinning test, inspect bots entering view, scopes, rapid 180-degree turns, doorways, prone/crouch transitions, weapons, muzzle flashes, and hit registration. Any missing/frozen/popping body or equipment is a failure; disable Num5 immediately and retain the report.
13. Confirm original renderer properties restore when approaching, becoming visible, pressing Num5/Num6, ending the raid, and quitting. Confirm the local player and remote-human teammates are unchanged.
14. Run the separate baseline/shadow/skinning repetitions in `BENCHMARK_PROTOCOL.md`. Do not enable both experiments until each passes independently.
15. Roll back with `scripts/rollback-test.ps1`; add `-RemoveConfig` only if the suite's own config should also be removed.

Any classification uncertainty must result in no shadow change. If a circuit breaker opens, keep the logs/report and leave the feature disabled.
