# Research and implementation choices

Primary sources consulted before implementation:

- BepInEx basic plugin development: <https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/index.html>
- BepInEx runtime patching: <https://docs.bepinex.dev/master/articles/dev_guide/runtime_patching.html>
- BepInEx preloader patchers (future only): <https://docs.bepinex.dev/master/articles/dev_guide/preloader_patchers.html>
- BepInEx debugging: <https://docs.bepinex.dev/master/articles/advanced/debug/index.html>
- Harmony patching and priority: <https://harmony.pardeike.net/articles/patching.html> and <https://harmony.pardeike.net/articles/priorities.html>
- Unity 2022.3 `ProfilerRecorder`: <https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html>
- Unity 2022.3 `SkinnedMeshRenderer`: <https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SkinnedMeshRenderer.html>
- Fika public source: <https://github.com/project-fika/Fika-Plugin>
- Fika headless client: <https://wiki.project-fika.com/advanced-features/headless-client>
- SPT organization and 4.0.13 release: <https://github.com/sp-tarkov> and <https://github.com/sp-tarkov/build/releases/tag/4.0.13>
- Official EFT 1.0.4.0 notes (client optimization and updated player culling on Customs, Interchange, and Reserve): <https://steamcommunity.com/games/3932890/announcements/detail/521994198580199494>
- Official EFT 1.0.4.5 notes (animation/audio resource use, player culling rollout, and Streets object/geometry optimization): <https://steamcommunity.com/games/3932890/announcements/detail/685251502317503193>
- Official EFT 1.0.5.0 notes (animation CPU reduction, audio leak fix, and experimental CPU-to-GPU light processing): <https://steamcommunity.com/app/3932890/discussions/1/842880822501639244/>

The installed DLLs remain the authority. ILSpy inspection verified installed Fika `Update`, `ManualStateUpdate`, `ObservedPlayers`, `ServerFPS`, and `IsObservedAI`; EFT `Player`, `GameWorld`, `PlayerBody`, player lists, local flag, AI data, renderer methods; and SPT `ModulePatch` constructors plus `Enable`/`Disable` conventions.

BepInEx documentation confirms normal plugins are loaded from `BepInEx/plugins` and runtime patching does not permanently rewrite game assemblies. Harmony documentation supports simple static prefix/postfix patches and explicit owner/priority compatibility. Unity 2022.3 documentation confirms recorder enumeration in player builds and the need to dispose unmanaged recorder resources.

Fika's documentation says a headless raid host offloads AI calculations and other host work, while every playing client remains responsible for its own observed-player networking and presentation. Installed Fika 2.3.3 confirms this split: `FikaClient.Update()` polls packets and applies snapshots for each observed player, while observed-player late/visual updates execute on the playing client. The suite therefore leaves SAIN/BigBrain untouched and targets only client representation.

Unity documents that disabling `SkinnedMeshRenderer.updateWhenOffscreen` stops offscreen skinned-mesh updates and can affect bounds for animation that exceeds imported bounds. The experiment is therefore distance-, visibility-, and hold-time-gated, immediately reversible, and disabled by default.

No external mod source code was copied. The implementation is original and uses public APIs plus installed metadata inspection, so third-party source-license compatibility did not need code-attribution handling.

## Version 0.4 findings and mapping

The official 2026 releases consistently target player culling, animation CPU use, audio resource use, and Streets environment/geometry. The installed 0.16.9 client already contains job-based observed-player culling and Fika wires remote players into it. Version 0.4 therefore consumes that existing authoritative visibility state to stop hidden animator work and amortize hidden prop/trigger-search presentation. It does not add per-player raycasts, alter packets, or throttle headless AI logic.

The live game's newer Streets geometry and light-instancing assets cannot be safely recreated from the older SPT client DLL alone. Backporting them would require matching map bundles, shaders, serialized culling data, and the newer engine-side implementation. Version 0.4 does not pretend that a config toggle can reproduce those assets.

Across the collected Customs captures, large 118-140 ms frames recur at approximately 15.05-second intervals regardless of visible AI, draw calls, or server activity. Installed SPT Detailed Bot Counter 1.7 is configured for 15 seconds and its update performs `Object.FindObjectOfType<GameWorld>()` before counting the already-available registered-player list. Version 0.4 exact-target patches that single call to use the suite's lifecycle-cached `GameWorld`; if the expected call is not found exactly once, the patch refuses to apply and leaves the external mod unchanged.
