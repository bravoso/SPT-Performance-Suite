# Reserve raid diagnostic - 2026-07-13

Five consecutive 120-second captures were recorded on `RezervBase`, totaling 60,431 frames. Average FPS ranged from 91.90 to 130.95. The slowest five percent of ordinary frames were 13.95 ms or longer (about 71.7 FPS), while isolated hitches reached 109-186 ms.

## Finding

Reserve is predominantly CPU main-thread bound in this session. Typical CPU main-thread time was 8.0-11.3 ms on average and 11.1-15.0 ms at p95. Typical GPU time was only 3.0-4.4 ms, render-thread time was 1.8-3.3 ms, and presentation/target-FPS waits were effectively zero. The GPU therefore had substantial unused headroom because the main thread was not feeding it quickly enough.

| Capture | Average FPS | 5th-percentile FPS | 1-second minimum FPS | CPU main average / p95 | Render average / p95 | GPU average / p95 |
|---|---:|---:|---:|---:|---:|---:|
| 23:11 | 108.46 | 75.38 | 40.77 | 9.65 / 13.26 ms | 2.89 / 5.11 ms | 4.12 / 6.42 ms |
| 23:14 | 96.61 | 66.96 | 34.96 | 10.94 / 14.91 ms | 2.55 / 4.75 ms | 3.82 / 6.37 ms |
| 23:16 | 91.90 | 66.72 | 48.86 | 11.30 / 14.99 ms | 3.27 / 5.48 ms | 4.42 / 6.80 ms |
| 23:19 | 98.98 | 74.96 | 42.57 | 10.51 / 13.33 ms | 2.54 / 3.62 ms | 3.56 / 4.67 ms |
| 23:22 | 130.95 | 90.02 | 67.86 | 8.00 / 11.11 ms | 1.85 / 2.98 ms | 3.02 / 4.02 ms |

The raw 30-40 FPS values were mostly hitch clusters rather than sustained low-rate periods: the longest consecutive streak below 40 FPS was three frames. One capture nevertheless reached a 34.96 FPS one-second rolling average because several large stalls landed close together.

## What grows in slow frames

Compared with normal frames, the slowest five percent added roughly 3.8-5.1 ms of measured CPU-main work. They also added 194-865 SetPass calls and 249-1,375 draw calls, while GPU cost usually increased by only 0.5-2.5 ms. Visible AI rose sharply in the clearest capture (+2.95 in the slowest five percent), but total AI count had weak or negative correlation because population changes slowly and does not describe combat activity or what is visible.

This points toward CPU-side visibility, animation, culling, and render submission work that scales with active/visible characters and scene complexity. Corpse count rose from about 2 to 30 through the raid, but its relationship with frame time was inconsistent, so Reserve does not support calling corpses the primary bottleneck.

Rare garbage collections caused individual 51-65 ms stalls in four captures. They contribute to visible hitches but do not explain the steady CPU cost.

## Instrumented methods

`EFT.Player.LateUpdate` was the largest measured steady method at about 0.65-0.97 ms per invocation, and it is also patched by `com.vai.spmc` (VAI-SchizoPMC). Across all characters, `Player.UpdateTick` / nested `ComplexUpdate` consumed about 0.51-1.09 ms in the sampled end frames, `ComplexLateUpdate` about 0.43-0.68 ms, and `ArmsUpdate` about 0.32-0.72 ms. Nested measurements must not be added together.

Fika's observed-player state update was small at roughly 0.05-0.07 ms total in sampled frames. `FikaClient.Update` was normally about 0.07-0.12 ms, but recorded rare maxima of 8.7-41.6 ms, making it a plausible source of occasional network/state hitches rather than the sustained bottleneck.

The currently instrumented methods account for only a minority of the 8-15 ms main-thread budget. The next profiling pass should therefore add time-series and spike-window measurements for SAIN/BigBrain combat decisions, animation/IK, physics, visibility/culling, render submission, and allocation/GC activity. It should also break `Player.LateUpdate` into child work and record the effect of VAI-SchizoPMC's patch.

All five captures were made with `RemoteCharacterShadowLOD=disabled`. They establish a baseline only; they do not measure whether the shadow feature helps.

## Measurement notes

Unity's `PlayerLoop` counter correctly captured the 109-186 ms hitch frames. Several end-of-frame CPU/GPU counters were temporally offset on those exact CSV rows, so isolated hitches should be analyzed with adjacent-frame spike windows rather than assuming every recorder value belongs to the same displayed frame. One invalid sentinel-sized GPU sample was discarded from the final capture.
