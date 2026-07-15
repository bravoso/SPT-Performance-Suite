# Changelog

## 1.0.0 - 2026-07-15

- Promote the complete client, loading, headless, and SPT server suite to the first production release.
- Replace the hardware/map-specific preset names with general `Balanced`, `Performance`, `Extreme`, and `Custom` profiles. `Extreme` is now the strongest default and keeps full texture mip quality with LOD selection untouched.
- Start silently: diagnostics overlay, bot-counter HUD, method profiler, client loading reports, and server reports are disabled by default but remain available in F12.
- Persist every gameplay configuration edit immediately, apply changed runtime values without a restart, and automatically move to `Custom` when an individual optimization value is edited.
- Keep all behavior-changing optimization modules enabled in every built-in preset and retain Num4 as the complete same-raid A/B switch.
- Document clean behavior without Fika, Dynamic Maps, SAIN, ORBIT, BigBrain, the optional server companion, or the bundled PiP replacement.
- Add an MIT project license, complete third-party attribution, production installation guide, and SPT Forge-ready description.

## 0.16.3 - 2026-07-15

- Fix the Streets loading-map stall at 53% caused by starting multiple Unity scene operations with activation deferred. Unity blocks its asynchronous-operation queue behind the first deferred scene, so the later scenes could never reach EFT's prepared state.
- Replace the unsafe parallel-scene path with bounded concurrent AssetBundle preload batches. Bundle I/O and decompression may overlap, while scene creation and activation remain in EFT's original serial order.
- Add a configurable scene-bundle preload concurrency limit (default 4) and report its bundle count and wall time.
- Preserve a fail-open path: if the concrete asset manager is unavailable, loading continues through the ordered EFT scene path without bundle preloading.

## 0.16.2 - 2026-07-15

- Enable EFT's built-in parallel path for additive raid-map scene sections while preserving the base-scene and activation order.
- Use Unity's hardware-specific maximum job-worker count during loading and restore the original count when the local player is ready.
- Parallelize the read-only item-to-bundle classification pass before asset-pool creation, with a count threshold, worker limit, and fail-open fallback.
- Parallelize Fika host/headless loot descriptor serialization across top-level loot trees while preserving packet order; client-side item construction remains unchanged because the item factory is not thread-safe.
- Replace EFT's quadratic static-container/loot presence scan with a linear ID set lookup before loot objects are instantiated.
- Raise the aggressive loading preset to a 256 MB system-memory upload ring buffer (512 MB while EFT loads raid scenes); retain a 16 ms upload slice because EFT doubles it to Unity's supported 32 ms ceiling during that stage.
- Extend loading reports with parallel scene counts, item-preparation counts/wall time, and the active upload/job settings.
- Add PID and client/headless role to loading report names so simultaneous processes cannot overwrite one another.
- Keep Unity GameObject, renderer, collider, physics, pool instantiation, scene activation, culling-cache finalization, and spatial-audio integration on the main thread where required.

## 0.16.1 - 2026-07-15

- Target the capture's three largest managed costs directly: `Player.ComplexLateUpdate`, `Player.ArmsUpdate`, and `AmbientLight.LateUpdate`.
- Beyond 50 metres, visible Fika observed proxies update arms, body, IK and complex-late presentation at half rate. Hidden proxies beyond 50 metres freeze those presentation passes completely after the configured visibility hold; Fika snapshot interpolation and audio continue.
- Apply remote presentation suppression only to Fika `ObservedPlayer` proxies on a playable client. The headless authority and ordinary SPT bots retain full hands, ballistics, movement and AI updates.
- Rate-limit only `AmbientLight`'s expensive persistent command-buffer rebuild: 15 Hz for Old CPU and 12 Hz for Streets Extreme. Ambient/stencil commands still execute every rendered frame, while camera/resolution changes retain EFT's own rebuild path.
- Add live counters for visible distant proxies, fully frozen hidden proxies, and skipped ambient command rebuilds.

## 0.16.0 - 2026-07-14

- Package the unmodified MIT-licensed PiP-Disabler 1.5.0 implementation. Num3 now changes between its live main-camera zoom/reticle/lens path and full-resolution vanilla PiP; no frozen-image camera stop remains.
- Restore every suite preset's vanilla PiP scale to 1.0. Reduced resolution remains an explicit F12 slider choice, not the default.
- Add detailed loading-stage timing for Fika map preparation, culling cache, bundle loading, and loot loading so the next report separates local Unity work from SPT HTTP time.
- Add direct headless profiling targets for ORBIT navigation/movement/action/strategy, SAIN bot/vision/hearing/decision updates, and BigBrain agent/layer updates.
- Add headless-only authority pacing: retain complete bot snapshots at 20 Hz and spread ORBIT navigation over at most three path calculations per frame. No player, damage, sound, inventory, or weapon state is filtered.
- Latest six Streets captures averaged 71.0 FPS with a 44.2 FPS fifth percentile. Main-thread medians ranged from 12.6 to 17.5 ms while GPU time ranged from 5.3 to 7.6 ms, confirming a CPU-side limit. Measured Fika plus observed-player synchronization was about 0.3 ms/frame, so broad packet suppression cannot recover the missing 4-7 ms.

## 0.15.1 - 2026-07-14

- Fix the early black-screen/headless startup deadlock introduced by directly waiting on SPT async requests from Unity's main thread. The log stopped at `/showMeTheMoney/getPartialRagfairConfig` because its continuation needed the blocked Unity synchronization context.
- Keep the request optimization and timing support, but execute synchronous wrappers through a small reusable background worker pool. This preserves the context separation SPT requires without creating a new `Task.Run` scheduling hop for each call.

## 0.15.0 - 2026-07-14

- Add a separate client loading DLL. It applies a 16 ms/128 MB Unity asset-upload budget, high background-loading priority, an above-normal process/main-thread priority and a pre-grown worker pool only while loading; all captured values are restored when the local player is ready.
- Stop the gameplay frame-pacing feature from forcing its low two-millisecond upload budget in menus and loading screens. It now activates only after raid start and releases its settings on raid end.
- Replace SPT client's synchronous `Task.Run(...).Result` request wrappers with direct async completion waits, removing an unnecessary worker dispatch without changing endpoints or response data.
- Add client loading reports that rank profile, configuration, bundle and raid requests by total/max milliseconds and mark slow endpoints.
- Add an optional SPT 4 server DLL. It pre-grows the ASP.NET worker pool, uses above-normal process priority, records startup/request timings and changes large zlib responses from maximum compression to fast compression to reduce profile/loot response CPU time.

## 0.14.0 - 2026-07-14

- Add living named-boss reporting for Reshala, Killa, Shturman, Glukhar, Sanitar, Tagilla, the Goons, Zryachiy, Kaban, Kollontay, Partisan and named cultists. Boss guards/followers remain counted separately.
- Classify Black Division, RUAF, RUAF Remnants and UNTAR before the generic boss rules using both MoreBotsAPI's registered IDs and resilient role-name fallbacks. Their installed server definitions declare `isBoss=false`, even though some use boss-prefixed enum names and boss-style AI settings.
- Add F12 options for installed font name, font style, font size, named-boss visibility and spawned-only category rows. The independent HUD still reuses the shared entity registry and performs no scene search.
- Replace the incorrect whole-map refresh cap with a selective 15-60 Hz minimap recenter budget. Dynamic Maps input, zoom, full-map movement and marker updates stay full-rate; all-map image preload blocking and lean marker selection remain.
- Remove the frozen-image `Keypad3` camera-stop experiment from the user interface. Reduced-resolution real PiP remains active; a future single-render scope mode must supply a usable main-camera reticle/lens path before it returns.

## 0.13.0 - 2026-07-14

- Removed the incorrect Dynamic Maps whole-`Update` rate cap. It throttled input/map motion while marker movement continued at full rate, so minimap motion is now smooth and uncapped; lean marker filtering and all-map image-preload blocking remain.
- Added `Keypad3` complete PiP rendering toggle. PiP-off stops the secondary optic camera for direct same-scene testing and maximum savings; the magnified scope image is intentionally unavailable/frozen until PiP is restored. Thermal/NVG optics remain protected by the existing special-optic option.

## 0.12.1 - 2026-07-14

- Restored Dynamic Maps extraction, extraction-status, secret-extraction, locked-door, transit, and dropped-backpack markers.
- Replaced six per-character hot-path dictionaries with staggered frame scheduling for hidden remote presentation; baked-hidden entities no longer read the weaker live visibility property on every callback.
- Added an aggressive world-presentation rate budget for the measured global culling, distant-shadow, deferred-decal, and weather callbacks. Game simulation, networking, damage, sound, and input remain full-rate.

## 0.12.0 - 2026-07-14

- Replace SPT Detailed Bot Counter with a suite-native living-bot HUD that reuses the shared entity registry at 2 Hz and is independent of the Num7 diagnostics panel. Add exact Black Division, RUAF, RUAF Remnant, and UNTAR role support without scene searches or per-refresh role strings.
- Add a Dynamic Maps compatibility budget. Preserve local/party position, quests, and player/party-killed corpses; disable unrelated live marker providers; keep the full map uncapped; and cap the always-on minimap update path to 24 Hz.
- Prevent Dynamic Maps from precaching layer images for every supported map. The selected map continues loading normally on demand, reducing unnecessary RAM/VRAM residency and startup uploads.
- Reduce capture interference by removing invocation timing from AreaLight methods measured above 150,000 calls/s and the cross-scene PerfectCulling group callback. Fix the Windows thread sampler with native thread CPU counters.
- Limit EFT's custom ambient reflection cubemap refresh to 10 Hz in aggressive presets while leaving direct lighting intact. Continuous preset captures now force compact CSV-only export instead of background JSON formatting.

## 0.11.0 - 2026-07-14

- Replace the one-frame/top-12 timing dump with a Minecraft-profiler-style cumulative 120-second CPU profile. Every called target is ranked by self and inclusive milliseconds, calls, average, approximate p95, maximum, main-thread time, worker-thread time, and one-core percentage.
- Accumulate Unity frame markers across every captured frame and sample real Windows CPU time by OS thread once per second. The report can now distinguish a saturated Unity main thread from worker/native load and quantify process-wide CPU use without per-frame process scans.
- Expand installed-mod discovery from only each plugin's root class to bounded MonoBehaviour frame callbacks throughout the plugin assembly. Profiling remains capture-only, uses fixed thread-local stacks, and performs no invocation logging.
- Add the `StreetsExtreme` F12 preset. It retains full texture mip quality while using shorter low-resolution shadow maps, fewer transient lights and particle raycasts, stronger hidden-remote presentation budgets, and the existing half-resolution real-PiP target.
- Record video-memory, render-texture, and graphics-buffer counters when exposed by the Unity player. These counters separate non-texture render allocation from total VRAM pressure; unavailable driver counters are reported honestly rather than estimated.

## 0.10.0 - 2026-07-14

- Add an authority-aware remote-combat relevance firewall. On non-host Fika clients, a distant shot becomes sound-only only when EFT's baked culling proves the shooter hidden and a conservative trajectory corridor proves the round cannot approach the local player.
- Preserve positional gunshot audio, replicated weapon/chamber state, authoritative headless damage, nearby combat, incoming fire, explosives, recently visible shooters, and both main-camera and PiP-camera visibility.
- Suppress hidden distant muzzle effects, offscreen impact particles/decals, casing physics, and remote character light contribution. Offline SPT and the Fika host retain all ballistic simulation and receive presentation-only reductions.
- Add per-frame benchmark counters for remote shots, sound-only conversions, safety bypasses, culled muzzle/impact/casing/light work, authority mode, and decision cost. Add capture timings for the Fika remote-shot handler, EFT bullet creation, muzzle effects, and hit effects.
- Keep the existing Fika shot packet as the cheap sound/state event instead of introducing a server protocol fork. Server-side filtering would remove only a small packet while requiring a replacement sound protocol and synchronized installation on every peer.

## 0.9.0 - 2026-07-14

- Analyze 31.5 minutes of Ground Zero and 4.3 minutes of Streets gameplay. Streets averaged 15.5 ms on the CPU main thread versus 5.3 ms on the GPU, with almost twice as many remote AI and twice the rate of 50+ ms hitch clusters.
- Feed EFT's stronger baked PerfectCulling visibility result into the suite's hidden-character budget. Fika's simpler frustum flag can no longer force full presentation work for distant characters already proven hidden behind map geometry.
- Add a conservative area-light command cache for Streets-heavy lighting. Non-shadowed, non-animated lights reuse their existing per-camera command buffers for a few frames; changed light state rebuilds immediately, refreshes are phase-staggered, and HDR/PiP remain untouched.
- Remove continuous profiler self-interference: rent and reuse large capture buffers, eliminate the full-buffer copy, default to compact CSV-only export, avoid per-number formatting strings, and stop repeating feature metadata on every CSV frame.
- Record explicit managed-used, managed-reserved, resident-memory, baked-culling, baked-hidden, and area-light cache counters. The ambiguous `gc_value` fallback no longer reports reserved memory as though it were per-frame allocation.
- Add capture-only timings for AreaLight pre-cull/command construction, ambient lighting, observed-player culling, PerfectCulling grid/camera work, and culling job completion.

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
