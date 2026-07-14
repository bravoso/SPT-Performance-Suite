# Benchmark protocol

Do not claim a performance gain from a single run.

## Baseline A

- Plugin installed.
- All optimization experiments disabled.
- Overlay may be visible.

## Test B

- Remote Character Shadow LOD enabled.
- Adaptive mode enabled with 120 m maximum, 60 m minimum, and 60 FPS target.
- Same map, location/direction, graphics, bot population, and Fika/headless configuration.

## Test C

- Remote Character Shadow LOD disabled.
- Remote AI Offscreen Skinning enabled.
- Same map, location/direction, graphics, bot population, and Fika/headless configuration.

## Procedure

1. Use a disposable SPT profile or controlled preset.
2. Use the same map and approximately the same location and viewing direction.
3. Allow at least 60 seconds of warm-up.
4. Press Num8 for the configured 120-second capture.
5. Repeat each state at least three times.
6. Compare average/minimum FPS; median, p95, and p99 frame time; main/render-thread time; bot and visible-bot counts; and Fika server FPS.
7. Repeat on Ryzen 7 5800X3D + RTX 4070 and i7-2600K + GTX 1070.
8. Review logs for circuit breakers, classification uncertainty, and restoration errors.
9. Visually verify nearby/local/remote-human shadows, remote-AI bodies and equipment, death/corpses, scopes, reconnect/transit, and raid exit.
10. Test Baseline A, Test B, and Test C independently before attempting B+C. Method timing should remain capture-only.

A feature is initially promising only with at least 1.0 ms lower median or p95 CPU frame time, or at least 5% repeatable FPS improvement, with no gameplay, synchronization, or visual regression. A smaller repeatable gain may remain if risk and overhead are negligible.

Captures are written under `BepInEx/plugins/TarkovPerformanceSuite/benchmarks`. CSV and JSON contain unavailable metrics as empty/`null`; this is expected.
