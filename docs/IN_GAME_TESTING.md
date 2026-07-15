# Exact in-game test steps

Use Woods on the i7-2600K as the acceptance run. The hideout is useful only for confirming that the old five-second periodic hitch is gone.

1. Make a separate disposable copy of the SPT installation; do not use the primary/reference folder.
2. Set `SPT_REFERENCE_ROOT` to the read-only reference and `SPT_TEST_ROOT` to the disposable copy.
3. Run `scripts/deploy-test.ps1`.
4. Start the disposable server/client and confirm the log shows `Tarkov Performance Suite 0.8.0 loaded` and the detected versions.
5. Confirm `Extreme` and `AllOptimizationsEnabled = true` are selected. Enable the diagnostics overlay for the test; it must say `OPTIMIZATIONS ON`.
6. Enter a raid. Confirm one raid-start log, gradual entity counts, Num7 overlay toggle, and unavailable counters displaying `n/a` without errors.
7. Press Num8, wait for completion, and verify matching CSV/JSON files plus the automatically generated timing report. Verify a second capture begins automatically, then press Num8 again and confirm the current partial capture is exported and profiling stops.
8. Press Num9 for an additional on-demand report. Verify runtime profiler values, method timings, and live Harmony owners. F12 remains the Configuration Manager.
9. Start continuous capture with Num8. Alternate Num4 ON and OFF every 30-60 seconds while moving through comparable Woods areas. Wait at least five seconds after each switch before judging it; the analyzer excludes those transients automatically.
10. Inspect bots entering view, rapid 180-degree turns, doorways, prone/crouch transitions, weapons, muzzle flashes, and hit registration. Hidden characters must resume full-rate arms/body/IK/late presentation as soon as they become visible. Any frozen or missing body/equipment is a failure; press Num4 to restore everything and retain the report.
11. Test a Vudu or TAC30 in both Num3 modes. Main-camera zoom must show a live reticle and correct lens/housing mask with no frozen texture. Full-resolution vanilla PiP must restore the independent optic view. Thermal/NVG scopes must remain usable through automatic bypass.
12. Confirm the overlay reports the original and target logical-processor counts. Confirm original CPU affinity, renderer, animator, camera, texture-quality, and frame-pacing properties restore after Num4 OFF, raid end, and quit.
13. On a client without SPT Detailed Bot Counter, remain in the hideout for at least 30 seconds and confirm there is no five-second frame-time spike.
14. Run at least two ON/OFF cycles in one raid; a single before/after run is too confounded by AI population and route changes.
15. Roll back with `scripts/rollback-test.ps1`; add `-RemoveConfig` only if the suite's own config should also be removed.

Any classification uncertainty must result in no shadow change. If a circuit breaker opens, keep the logs/report and leave the feature disabled.
