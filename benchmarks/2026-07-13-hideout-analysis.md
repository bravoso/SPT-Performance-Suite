# Hideout diagnostic run - 2026-07-13

The first 120-second capture contained 7,108 samples. It averaged 59.77 FPS and 16.885 ms frame time, with a 16.668 ms p95. This is effectively a 60 FPS-limited hideout run and does not reproduce the reported 30-40 FPS raid bottleneck.

The original 0.1.0 configuration had method timing disabled. Its generic `Main Thread` recorder averaged 16.884 ms but included waiting, while the exact CPU/GPU/wait markers exposed by Unity were not selected. Therefore it cannot identify the expensive subsystem.

Version 0.1.1 records exact CPU main/render/total, GPU, presentation wait, PlayerLoop, target-FPS wait, GC, rendering counts, and all exposed timing markers. Method aggregates reset at benchmark start and an F10-style report is exported automatically at completion.

No optimization conclusion should be drawn from the hideout capture. The next capture must be taken during the actual 30-40 FPS raid condition with method timing enabled.
