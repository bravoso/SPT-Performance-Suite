# Profiler metrics

The installed Unity 2022.3.43f1 `UnityEngine.CoreModule.dll` contains and successfully compiles these APIs:

- `Unity.Profiling.ProfilerRecorder`
- `Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable`
- `ProfilerRecorderHandle.GetDescription`

The actual counters in a release player cannot be determined from the DLL alone. At raid start the plugin enumerates every available handle, starts only matching supported handles, and writes `BepInEx/plugins/TarkovPerformanceSuite/AVAILABLE_PROFILER_METRICS.runtime.md`.

Version 0.1.1 records every enumerated `TimeNanoseconds` marker except the uninitialized placeholder, plus the following counters when enumeration confirms them:

- Main Thread
- Render Thread
- GC Allocated In Frame
- GC Reserved Memory
- System Used Memory
- Draw Calls Count
- Batches Count
- SetPass Calls Count
- Triangles Count
- Vertices Count
- Shadow Casters Count
- Visible Skinned Meshes Count

The inspected runtime exposed separate `CPU Main Thread Frame Time`, `CPU Render Thread Frame Time`, `CPU Total Frame Time`, `GPU Frame Time`, `FrameTime.GPU`, `Gfx.WaitForPresentOnGfxThread`, `PlayerLoop`, `WaitForTargetFPS`, and `GC.Collect` markers. All are converted from nanoseconds to milliseconds in the overlay, CSV/JSON capture, and Num9 report.

Missing values display as `n/a` and serialize as an empty CSV field / JSON `null`. Every recorder is disposed at raid end and plugin destruction.

Entity metrics come from the amortized `GameWorld.RegisteredPlayers` registry, not a per-frame scene scan. Animator, skinned-renderer, visibility, and shadow counts therefore cover registered player representations. `ObservedPlayersCorpses` is a partial observed-corpse count. Global particle, audio-source, and corpse-rigidbody counts are intentionally unavailable in 0.1.0 because a safe lifecycle registry was not verified and full-scene scans would contaminate measurements.
