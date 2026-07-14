# Changelog

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
