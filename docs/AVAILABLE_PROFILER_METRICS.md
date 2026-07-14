# Profiler metrics

The installed Unity 2022.3.43f1 `UnityEngine.CoreModule.dll` contains and successfully compiles these APIs:

- `Unity.Profiling.ProfilerRecorder`
- `Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable`
- `ProfilerRecorderHandle.GetDescription`

The actual counters in a release player cannot be determined from the DLL alone. At raid start the plugin enumerates every available handle, starts only matching supported handles, and writes `BepInEx/plugins/TarkovPerformanceSuite/AVAILABLE_PROFILER_METRICS.runtime.md`.

Requested names, used only when enumeration confirms them:

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

Missing values display as `n/a` and serialize as an empty CSV field / JSON `null`. Every recorder is disposed at raid end and plugin destruction.

Entity metrics come from the amortized `GameWorld.RegisteredPlayers` registry, not a per-frame scene scan. Animator, skinned-renderer, visibility, and shadow counts therefore cover registered player representations. `ObservedPlayersCorpses` is a partial observed-corpse count. Global particle, audio-source, and corpse-rigidbody counts are intentionally unavailable in 0.1.0 because a safe lifecycle registry was not verified and full-scene scans would contaminate measurements.

