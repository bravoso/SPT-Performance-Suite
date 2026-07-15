# Tarkov Performance Suite 1.0.0

First production release of the client performance suite, loading accelerator, Fika/headless integrations, and optional SPT server accelerator.

Highlights:

- Extreme general-purpose preset enabled by default; Balanced, Performance, and Custom remain available in F12.
- Diagnostics overlay, bot HUD, method profiler, and loading/server reports are off by default.
- Runtime gameplay settings save immediately and update live.
- No hard dependencies on Fika, Dynamic Maps, SAIN, ORBIT, or BigBrain.
- Full-resolution vanilla PiP or the included PiP-Disabler 1.5.0 main-camera replacement.
- Client/headless loading parallelism and optional SPT server startup/response acceleration.
- MIT project license and complete PiP-Disabler attribution.

The conservative entity-matched Customs comparison measured +14.9% average FPS, +30.8% 5% low, +10.5% 1% low, -10.9% median main-thread time, and -23.2% 95th-percentile main-thread time. Results vary by raid and hardware and are not guaranteed.

Install by extracting `TarkovPerformanceSuite-1.0.0.zip` into the SPT game folder. With a remote SPT server, install the packaged `SPT\user\mods\TarkovPerformanceLoadingServer` directory on the actual server machine.

SHA256: `1A4FE8408D2A21B48737D8367ACF6113992D136214A12553A7AB5E36E242CB39`
