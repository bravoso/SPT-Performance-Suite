# Exact in-game test steps

These steps are pending because `SPT_TEST_ROOT` was not found.

1. Make a separate disposable copy of the SPT installation; do not use the primary/reference folder.
2. Set `SPT_REFERENCE_ROOT` to the read-only reference and `SPT_TEST_ROOT` to the disposable copy.
3. Run `scripts/deploy-test.ps1`.
4. Start the disposable server/client and confirm the log shows `Tarkov Performance Suite 0.1.0 loaded` and the detected versions.
5. Confirm `BepInEx/config/com.lucaswilluweit.tarkovperformancesuite.cfg` exists and the shadow experiment is `false`.
6. Enter a raid. Confirm one raid-start log, gradual entity counts, Num7 overlay toggle, and unavailable counters displaying `n/a` without errors.
7. Press Num8, wait for completion, and verify matching CSV/JSON files plus the automatically generated timing report.
8. Press Num9 for an additional on-demand report. Verify runtime profiler values, method timings, and live Harmony owners. F12 remains the Configuration Manager.
9. Enable `RemoteCharacterShadowsDryRun=true` and the experiment. Press Num6 as needed, or use the Boolean toggle in F12. Verify counters change but renderer shadows do not.
10. Disable dry-run, keep 120 m initially, and verify only distant confirmed AI shadows change. Approach/retreat, change AI equipment where possible, kill an AI, transit/reconnect if used, and exit the raid.
11. Confirm original shadows restore when approaching, pressing F12, ending the raid, and quitting the plugin/client. Confirm local-player and remote-human teammate presentation is unchanged.
12. Run the baseline/test repetitions in `BENCHMARK_PROTOCOL.md`.
13. Roll back with `scripts/rollback-test.ps1`; add `-RemoveConfig` only if the suite's own config should also be removed.

Any classification uncertainty must result in no shadow change. If a circuit breaker opens, keep the logs/report and leave the feature disabled.
