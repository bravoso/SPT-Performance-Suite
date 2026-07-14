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

The installed DLLs remain the authority. ILSpy inspection verified installed Fika `Update`, `ManualStateUpdate`, `ObservedPlayers`, `ServerFPS`, and `IsObservedAI`; EFT `Player`, `GameWorld`, `PlayerBody`, player lists, local flag, AI data, renderer methods; and SPT `ModulePatch` constructors plus `Enable`/`Disable` conventions.

BepInEx documentation confirms normal plugins are loaded from `BepInEx/plugins` and runtime patching does not permanently rewrite game assemblies. Harmony documentation supports simple static prefix/postfix patches and explicit owner/priority compatibility. Unity 2022.3 documentation confirms recorder enumeration in player builds and the need to dispose unmanaged recorder resources.

Fika's documentation says a headless raid host offloads AI calculations and other host work, while every playing client remains responsible for its own observed-player networking and presentation. Installed Fika 2.3.3 confirms this split: `FikaClient.Update()` polls packets and applies snapshots for each observed player, while observed-player late/visual updates execute on the playing client. The suite therefore leaves SAIN/BigBrain untouched and targets only client representation.

Unity documents that disabling `SkinnedMeshRenderer.updateWhenOffscreen` stops offscreen skinned-mesh updates and can affect bounds for animation that exceeds imported bounds. The experiment is therefore distance-, visibility-, and hold-time-gated, immediately reversible, and disabled by default.

No external mod source code was copied. The implementation is original and uses public APIs plus installed metadata inspection, so third-party source-license compatibility did not need code-attribution handling.
