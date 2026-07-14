# Changelog

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
