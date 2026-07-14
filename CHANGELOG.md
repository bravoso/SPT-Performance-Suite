# Changelog

## 0.8.0 - 2026-07-14

- Remove every PiP HDR and refresh-rate override. The optimizer no longer disables the optic camera or calls `Camera.Render()` manually; Tarkov/Unity owns camera scheduling and HDR, while the suite only reduces normal-optic render resolution and optionally disables MSAA.
- Add a combat-presentation budget for distant ejected casings. Casings beyond 25 m skip `BouncingObject.Init` trajectory raycasts and per-frame rotation/physics, without changing projectiles, weapons, hit detection, damage, or nearby casing audio.
- Cap the client-only active-bullet flyby-audio scan at 30 Hz. Actual ballistics, gunshot playback, hit effects, and network processing remain untouched.
- Expand capture-only method timing to EFT's `GameWorldUnityTickListener`, individual world-tick/player/ballistics stages, culling manager, distant shadows, effects, shell activation/update, bullet-sound controller, and job scheduler.
- Analyze the live Customs A/B data: stable samples were CPU-main limited at roughly 8-11 ms versus 4-5 ms GPU, and the worst frames rose most strongly with visible entities, draw calls, and SetPass work. Existing Fika client/player methods were small in this light raid, so network polling is deliberately left full-rate.

## 0.7.0 - 2026-07-14

- Treat sustained client throughput as a separate target from hitches. The friend Woods capture resolves to about 22.1 ms of stable main-thread work versus 10.1 ms of GPU work: roughly 5.5 ms must be removed for 60 FPS and 9.6 ms for 80 FPS at the same scene workload.
- Add a reversible all-logical-processor experiment for older 4-core/8-thread CPUs. It records the original process-affinity mask, exposes every available logical processor during raids, rechecks it without scanning, and restores the original mask on Num4 OFF, raid end, or shutdown.
- Attach the hidden-remote presentation budget directly to Fika `ObservedPlayer.ObservedVisualPass` and `ObservedFBBIKUpdate` overrides. Network snapshots, visibility, movement roots, combat, and simulation remain full-rate.
- Expand capture-only method timing to verified EFT spatial audio, ambient audio, sound-player loops, and every installed BepInEx plugin's declared parameterless frame callbacks. This is intended to identify the currently unattributed main-thread milliseconds and distant-gunshot slowdowns.
- Report the CPU savings required for 60/80/100 FPS and the same-workload GPU ceiling in the offline analyzer. Method timing is enabled by default but records only during Num8 captures.

## 0.6.0 - 2026-07-14

- Enable the complete `OldCpuAggressive` feature stack by default. Num4 now atomically toggles all behavior-changing optimizations while diagnostics and capture continue running.
- Record `optimizations_enabled` on every CSV/JSON sample and teach the analyzer to compare ON/OFF segments after excluding the first five transient seconds following each switch.
- Add a real-PiP scope budget: Tarkov's secondary optic camera keeps its vanilla refresh cadence and HDR state, while normal optics can use a smaller render texture and no MSAA. Thermal and night-vision optics bypass it.
- Add optic source/optimized resolution and active-state counters to reports and the overlay.
- Extend the hidden-character budget to `Player.ComplexLateUpdate` and increase the old-CPU divisor from 4 to 8 at a 25 m hidden-only threshold. Visibility, network, combat, and simulation passes remain untouched.
- Make the master switch restore camera, renderer, animator, quality, and frame-pacing state instead of requiring a restart. Unsafe forced LOD behavior remains removed.

## 0.5.0 - 2026-07-14

- Fix the severe regression seen on an i7-2600K: when SPT Detailed Bot Counter was absent, the compatibility module performed a loaded-type search every five seconds. Friend captures showed a repeatable 460-490 ms freeze in both Woods and the hideout. Optional-mod detection is now a one-time constant-time plugin-registry lookup.
- Remove all forced remote-AI LOD behavior and stop changing Unity `maximumLODLevel` or `lodBias`. Benchmark columns remain as zero-valued compatibility fields so existing analysis tools keep working.
- Remove dry-run settings and branches from the F12 interface, configuration templates, and active shadow, skinning, and declutter implementations.
- Make Num8 toggle continuous profiling. While enabled, each completed capture exports its CSV/JSON and diagnostic report, then the next capture begins automatically.
- Add exact in-raid suppression for RealisticFrag's per-projectile informational message while retaining every warning and error. The Woods session produced hundreds of these messages during distant gunfire.
- Extend the old-CPU hidden-character budget to arms, body, and inverse-kinematics presentation updates. Local-player, visibility, network interpolation, combat, and simulation updates remain full-rate.
- Record skipped hidden presentation updates in CSV/JSON and expose combat-log suppression in reports and the overlay.

## 0.4.0 - 2026-07-14

- Add a friend-ready `OldCpuAggressive` preset in the F12 Configuration Manager. It deliberately leaves global LOD, remote LOD forcing, and texture mip quality disabled.
- Remove the verified repeating full-scene `GameWorld` search in SPT Detailed Bot Counter 1.7 while preserving its counts and UI. The Reserve/Customs captures showed a matching 118-140 ms hitch roughly every 15 seconds.
- Add a remote-character CPU budget that reuses EFT/Fika's existing culling result. Confirmed hidden remote characters use complete animator culling and lower-frequency prop/trigger-search presentation work; visible characters and all gameplay/network state remain full-rate.
- Add CPU frame-pacing controls for low-priority background loading, bounded async upload work, a persistent upload buffer, and reusable Unity physics collision callbacks.
- Add a shared 20 Hz entity relevance snapshot and reuse component buffers to remove duplicate distance/visibility/health checks and periodic registry allocations.
- Add a compact overlay with current FPS, rolling 1% low, CPU main/render milliseconds, GPU milliseconds, bottleneck label, AI visibility, server FPS, and suite cost. Detailed mode remains available.
- Record remote-budget activity and compatibility-cache lookups in benchmark CSV/JSON exports.
- Add a self-contained Old CPU ZIP packaging workflow and plain-language installation guide.

## 0.3.0 - 2026-07-14

- Add an opt-in aggressive Unity render profile: earlier global LOD selection, fixed pre-raid texture mip limit, shorter shadows, two-bone skinning, reduced pixel lights, disabled realtime probes/soft particles, and a smaller particle-raycast budget.
- Add distant confirmed-remote-AI LOD forcing with cheaper skin quality, no skinned motion vectors, no reflection probes, forced no-motion vectors, and dynamic occlusion enabled.
- Add incremental renderer-only cosmetic decluttering with dry-run counters, conservative name/size/type filtering, and complete restoration.
- Surface aggressive-module counters and processing cost in the overlay and diagnostic report.
- Keep network state, AI authority, movement roots, combat, damage, inventory, and navigation untouched.

## 0.2.0 - 2026-07-14

- Added adaptive remote-AI shadow distance with sustained-pressure hysteresis and slow recovery.
- Added a disabled-by-default offscreen skinned-mesh update guard for distant confirmed remote AI.
- Made method timing capture-only by default to avoid continuous profiling overhead.
- Added exact timing for Fika `ObservedPlayer.LateUpdate`, `ManualUpdate`, `ObservedVisualPass`, and `ObservedFBBIKUpdate`, plus EFT physical/prop update buckets.
- Added Num5 as the offscreen-skinning toggle and expanded F12 configuration entries.

## 0.1.1 - 2026-07-13

- Moved suite hotkeys to the numeric keypad to avoid the F12 Configuration Manager conflict.
- Added exact CPU main/render/total, GPU, PlayerLoop, present-wait, frame-limit-wait, and GC timings.
- Added live millisecond snapshots to F10 reports and expanded high-level method timing targets.

## 0.1.0 - 2026-07-13

- Added environment discovery and safe reference/deployment scripts.
- Added minimal BepInEx lifecycle and version diagnostics.
- Added optional overlay, profiler metric discovery, benchmark capture, and report export.
- Added disabled-by-default Harmony method timing.
- Added disabled-by-default dry-run capable remote-AI shadow LOD experiment.
