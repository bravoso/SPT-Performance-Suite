# Tarkov Performance Suite 1.0

An experimental, profiler-first performance suite for SPT/Escape from Tarkov. It was built to answer one question:

> Why can Tarkov run at 30-60 FPS while a capable GPU is only 30-50% utilized, and how much unnecessary CPU work can be removed without breaking the game?

[Download the latest release](https://github.com/bravoso/SPT-Performance-Suite/releases/latest) · [Version 1.0.0 ZIP](https://github.com/bravoso/SPT-Performance-Suite/releases/download/v1.0.0/TarkovPerformanceSuite-1.0.0.zip) · [Changelog](CHANGELOG.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

## Important project status: AI-generated and looking for a maintainer

The Tarkov Performance Suite source was designed, generated, debugged, documented, and iterated with OpenAI Codex. Lucas Willuweit supplied the performance goals, installed environments, gameplay tests, profiler data, hardware observations, safety decisions, and acceptance/rejection feedback. The implementation itself was produced by an AI coding agent.

The included **PiP-Disabler 1.5.0 is the exception**: it is an existing project by Fiodorwellfme, packaged unmodified under its MIT license. It is not claimed as code produced by this project.

The [SPT Forge content guidelines](https://forge.sp-tarkov.com/content-guidelines) do not accept mods that are substantially or entirely written by AI coding agents. For that reason, this project is being shared directly through GitHub and is not presented as a Forge submission.

This is a working experiment, not a claim that an experienced Unity/EFT developer has manually audited every generated line. It works on the two development/test computers described below and produced repeatable improvements, but users should treat it as experimental software. If an experienced C#/Unity/SPT developer wants to review it, correct it, fork it, or take over maintenance, that is explicitly welcomed. The source, history, profiler rationale, failed experiments, tests, and binaries are public to make that possible.

## The problem in simple language

The GPU cannot independently produce the next Tarkov frame. Before the GPU can draw it, the CPU must update players, animations, culling, lights, particles, ballistics presentation, audio, UI, mods, and rendering commands. When the CPU finishes that work late, the GPU waits—even if Windows shows only moderate total CPU usage.

A game can be CPU-bound while showing 12%, 40%, or 50% overall CPU usage because one critical game thread can be saturated while other cores are partly idle. The render thread and GPU depend on work prepared by that critical path. More total cores do not automatically make Unity/EFT methods thread-safe or parallel.

The suite therefore does two things:

1. It measures where frame time is actually going, in milliseconds and cumulative CPU time.
2. It reduces, caches, rate-limits, or skips presentation work that is hidden, distant, unchanged, or irrelevant to the local player.

The objective is not to invent fake frames or blindly force 100% GPU usage. The objective is to shorten CPU frame preparation enough that the GPU receives work more consistently.

## What the suite does

### Hidden and distant characters

- Reuses EFT/Fika visibility and baked PerfectCulling results instead of performing new full-scene searches.
- Reduces arms, body, inverse-kinematics, animator, prop, trigger-search, and complex-late presentation work for confirmed hidden remote characters.
- Beyond the configured distance, visible Fika observed proxies can update expensive presentation at a lower frequency.
- Confirmed hidden distant Fika proxies can freeze presentation until they become relevant again.
- Keeps snapshot interpolation, movement roots, network state, authoritative AI, damage, inventory, weapons, sound, and visibility checks active.
- Never applies this presentation freeze to the local player, a playable headless authority, or ordinary authoritative SPT bots.

### Distant combat presentation

- Keeps nearby combat, incoming fire, damage, explosives, visible shooters, recently visible shooters, and positional gunshot audio.
- On a non-host Fika client, a distant hidden shot can become sound-only only when baked map culling proves the shooter is hidden and a conservative trajectory corridor proves that the shot cannot approach the local player.
- Suppresses eligible hidden/offscreen muzzle flashes, smoke, sparks, muzzle lights, impact particles, decals, casing physics, and remote flashlight contribution.
- Retains the authoritative projectile and damage calculation on the Fika host/headless.
- Leaves ordinary offline SPT ballistics authoritative on the local process.

### Lighting, shadows, culling, decals, weather, and particles

- Reuses command buffers for static, non-shadowed area lights when their state has not changed.
- Limits expensive ambient reflection and ambient command-buffer rebuilding while continuing to execute the existing light commands every frame.
- Budgets distant-shadow, global culling maintenance, deferred decal maintenance, and weather presentation.
- Reduces shadow distance/resolution, pixel lights, particle collision raycasts, realtime reflection probes, soft particles, and skin weights under aggressive profiles.
- Incrementally hides conservatively selected renderer-only cosmetic clutter; it does not destroy colliders or gameplay objects.
- Leaves EFT LOD selection untouched in 1.0. Earlier forced-LOD experiments were removed after they made objects disappear.
- Keeps the texture mip limit at zero in the default Extreme profile. The suite does not intentionally make Streets textures blurry.

### PiP scopes

- Vanilla PiP renders the world a second time through the optic camera, so magnified scopes amplify an existing CPU/render bottleneck.
- The suite leaves vanilla PiP at full resolution by default when vanilla PiP is selected. HDR is never disabled.
- Num3 switches between full-resolution vanilla PiP and the included PiP-Disabler main-camera zoom/reticle/lens implementation.
- Thermal, night-vision, and unsupported special optics retain their protected behavior.
- If PiP-Disabler is absent, the suite still loads and vanilla PiP remains available.

### Dynamic Maps and UI compatibility

- Dynamic Maps integration activates only when Dynamic Maps is installed.
- Keeps the local player, party, quests, friendly/party kills, extracts, extraction status, doors, transits, and dropped backpacks.
- Removes live enemy/scav/boss marker providers that were not needed for normal map use.
- Prevents unrelated map layer images from being precached at startup.
- Limits only the expensive always-on minimap recenter path; it does not throttle full-map input or movement.
- Includes an optional cached living-bot HUD that recognizes standard bosses, Black Division, RUAF, RUAF Remnants, and UNTAR without periodic scene searches. It is off by default in 1.0.

### Known mod hot paths

- Replaces verified periodic `GameWorld` scene searches in compatible mods with cached references.
- Suppresses only a verified per-projectile RealisticFrag informational log hot path; warnings and errors remain.
- Discovers Dynamic Maps, Fika, SAIN, ORBIT, and BigBrain by loaded assembly/plugin name. None is a hard DLL dependency.
- Missing or changed optional integrations fail open: the optional mod keeps its normal behavior instead of preventing EFT from booting.

### CPU scheduling and frame pacing

- Makes all available logical processors visible if another setting restricted the EFT process affinity mask.
- Restores the original affinity when the optimization master is disabled, the raid ends, or the plugin shuts down.
- Uses a small persistent asynchronous upload buffer during gameplay to reduce upload allocation churn and limit upload competition with the main frame.
- Enables Unity's reusable physics collision callbacks to reduce managed allocation pressure.
- Does **not** move arbitrary Unity GameObject, renderer, physics, inventory, AI, or EFT state methods onto worker threads. Those systems are not generally thread-safe.

### Client/headless loading accelerator

- Uses a larger temporary upload time slice and staging buffer while loading, then restores gameplay settings when the local player is ready.
- Pre-grows the managed worker pool and uses Unity's hardware-specific job-worker maximum during loading only.
- Parallelizes read-only item-to-bundle classification before main-thread pool creation.
- On a Fika host/headless, parallelizes independent top-level loot descriptor serialization while preserving packet order.
- Replaces a containers-times-loot nested lookup with a linear ID-set lookup.
- Preloads dependency-disjoint AssetBundle trees concurrently, then creates and activates Unity scenes in EFT's original serial order.
- Keeps GameObject creation, scene activation, renderers, colliders, physics, pools, culling finalization, spatial audio, and client item construction on their required thread.

### Optional SPT server accelerator

- Pre-grows the ASP.NET/.NET worker pool.
- Can use above-normal process priority.
- Uses fast zlib compression instead of maximum compression for large JSON responses, reducing server CPU time at the cost of slightly larger local-network responses.
- Can profile server startup and slow endpoints, but report generation is off by default in 1.0.
- Is optional. The client performance suite boots and works without it.

## What it deliberately does not do

- It does not reduce bot count or change bot difficulty.
- It does not change SAIN decisions, hearing, vision, or tactics.
- It does not discard authoritative player/bot snapshots.
- It does not change hit registration, damage, inventory, quests, extracts, movement roots, or weapon state.
- It does not promise to make every CPU method multithreaded; unsafe parallelism can deadlock or corrupt Unity state.
- It does not promise 100% GPU usage. A VRAM, GPU, frame-cap, driver, thermal, or engine limit can remain.
- It does not solve Streets' texture residency problem. On the 12 GB RTX 4070 test system, Streets reached approximately 11.8-12.0 GB of reported VRAM use even with medium textures and Streets low-memory mode.
- It does not guarantee the same result on every raid. Bot population, combat, weather, route, optics, other mods, host placement, and cache state change the workload.

## How the project was debugged

The suite began as a profiler, not as a list of random graphics tweaks. Each optimization was introduced after captures identified a cost or a repeatable hitch, and unsafe ideas were removed when testing contradicted them.

### Retained test evidence

The primary development installation currently retains:

| Evidence | Retained amount |
|---|---:|
| Benchmark CSV captures | 103 |
| Sampled frames | 1,008,652 |
| Total benchmark time | 12,419 seconds / 3.45 hours |
| JSON benchmark companions | 74 |
| Full diagnostic reports | 103 |
| Minecraft-style cumulative CPU profiles | 32 |
| Deep-profile time | 3,758 seconds / 62.6 minutes |
| Timed managed method calls in those profiles | 234,296,031 |
| Client/headless loading reports | 24 |
| Total retained generated report artifacts | 336 |

The 103 CSV captures cover:

| Map identifier | Human name | Captures |
|---|---|---:|
| `TarkovStreets` | Streets of Tarkov | 32 |
| `bigmap` | Customs | 29 |
| `Sandbox_high` | Ground Zero | 17 |
| `RezervBase` | Reserve | 10 |
| `hideout` | Hideout test environment | 10 |
| `Woods` | Woods | 5 |

The friend Interchange test was a live gameplay observation and was not captured by this retained profiler set, so it is reported separately rather than silently mixed into the CSV statistics.

The repository itself contains 37 C# files and roughly 9,335 lines of C# across the core, gameplay plugin, loading plugin, server plugin, and tests. The 1.0 release commit changed 50 tracked files. These counts describe scope, not a claim of human review quality.

### How the profiler evolved

1. Version 0.1 recorded FPS plus CPU main/render time, GPU time, PlayerLoop, present waits, frame-cap waits, GC, entity counts, and environment versions.
2. Numpad controls replaced conflicting function keys so F12 remained available for Configuration Manager.
3. Capture-only Harmony timing added verified EFT and Fika methods without logging every call.
4. The initial top-method view was replaced with a cumulative 120-second profile inspired by Minecraft profiling reports.
5. The profiler learned self time, inclusive time, call count, average, approximate p95, maximum, main-thread/worker-thread split, and percentage of one core.
6. It expanded into bounded installed-mod `Update`/`LateUpdate` discovery, Unity profiler markers, Windows per-thread CPU sampling, VRAM/render-target counters, loading stages, Fika, SAIN, ORBIT, and BigBrain targets.
7. Profiling overhead was then reduced through capture-only activation, rented/reused buffers, CSV-first export, fixed thread-local stacks, removed ultra-hot targets, and silent-by-default 1.0 recorders.

One later 120-second Customs profile patched 253 methods, saw 121 of them execute, and timed 4,287,226 calls. Across the 32 retained deep profiles, the suite timed more than 234 million managed calls. Native Unity/driver work is reported through frame markers because Harmony method timing cannot attribute code that is outside managed methods.

### The major findings

The stress profile that changed development priorities identified these cumulative managed costs:

| Method | Cumulative time | Calls | Why it mattered |
|---|---:|---:|---|
| `EFT.Player.ComplexLateUpdate` | 4,397.655 ms | 261,504 | Remote player late presentation was being repeated at entity scale. |
| `EFT.Player.ArmsUpdate` | 3,024.357 ms | 186,456 | Arms/hands presentation was expensive across remote observed players. |
| `AutoEFT.AmbientLight.LateUpdate` | 1,966.105 ms | 9,274 | Ambient lighting rebuilt persistent render commands too frequently. |

This directly produced the hidden/distant observed-player presentation budget and the ambient command-buffer refresh budget in 0.16.1.

Other important discoveries:

- Streets was usually CPU-main limited, not GPU limited. Six representative Streets captures averaged 71.0 FPS with a 44.2 FPS fifth percentile; main-thread medians ranged from 12.6 to 17.5 ms while GPU time ranged from 5.3 to 7.6 ms.
- Ground Zero and Streets analysis showed Streets at roughly 15.5 ms main-thread time versus 5.3 ms GPU time, nearly twice the remote AI and roughly twice the 50+ ms hitch-cluster rate.
- Measured Fika client/observed synchronization was approximately 0.3 ms per frame in the inspected Streets capture. Broadly suppressing network packets could not recover the missing 4-7 ms and would create correctness risk.
- Visible entities, draw calls, SetPass calls, player late presentation, area/ambient lighting, Dynamic Maps, and some installed-mod frame callbacks were more meaningful than raw network polling in the tested raids.
- The old SPT Detailed Bot Counter performed a full-scene search roughly every 15 seconds, matching 118-140 ms hitches. Another fallback optional-mod scan caused 460-490 ms freezes every few seconds on the i7-2600K when the expected mod was absent.
- Distant gunfire created visible drops because the client still performed bullet presentation, muzzle effects, impacts, lights, casing work, and flyby scans even when the fight was far away and hidden.
- PiP was an amplifier rather than the root problem: it asks Tarkov to render another camera after the CPU has already struggled to prepare the main view.

### Failed experiments are part of the history

This project did not keep every AI-generated idea:

- Forced remote LOD and global LOD bias made objects disappear and were removed.
- Lower-resolution PiP made long-range aiming unpleasant and was removed from the default profiles.
- Disabling PiP by merely stopping the camera left a frozen optic image and was replaced by PiP-Disabler's complete reticle/lens solution.
- Disabling optic HDR broke scopes and was removed permanently.
- A whole-Dynamic-Maps `Update` cap made the map motion/input feel wrong and was replaced with a selective minimap recenter budget.
- A direct synchronous wait during startup created a black-screen/headless deadlock because the continuation required the blocked Unity synchronization context. Version 0.15.1 moved that work to reusable background request workers.
- Starting multiple deferred-activation Unity scene operations caused Streets loading to stop at 53%. Version 0.16.3 retained concurrent bundle preload but restored serial scene creation/activation.

These failures are documented because they demonstrate why “just multithread it” is dangerous inside Unity.

## Performance results

### How to interpret the comparison

There are three different evidence levels below:

- **CSV comparison:** calculated from saved frame samples.
- **Same-raid A/B observation:** Num4 toggled the behavior-changing stack while the same raid continued.
- **User report:** visually observed FPS without a corresponding retained profiler capture.

Num4 OFF is the closest controlled comparison with original/vanilla Tarkov behavior because it restores the modified camera, renderer, animator, quality, frame-pacing, and affinity state. It is not a byte-for-byte vanilla executable: the DLL remains loaded, fail-open Harmony prefixes still perform their enable checks, and diagnostics can remain active if the user enabled them. The first v0.2 Customs captures are also not pure vanilla; they already contained the early shadow/profiler code. The README therefore avoids labeling either dataset as a perfect laboratory vanilla benchmark.

### Primary system: Ryzen 7 5800X3D + RTX 4070 12 GB

The earliest retained Customs v0.2 captures were compared with the final pre-1.0 Customs dataset:

| Metric | Early Customs v0.2 | Latest Customs dataset | Raw change |
|---|---:|---:|---:|
| Average FPS | 92.67 | 116.80 | **+26.0%** |
| Median FPS | 98.66 | 123.37 | **+25.0%** |
| 5% low FPS | 65.39 | 88.28 | **+35.0%** |
| 1% low FPS | 47.28 | 60.44 | **+27.8%** |
| Median main-thread time | 10.141 ms | 8.105 ms | **-20.1%** |
| 95th-percentile main-thread time | 15.255 ms | 11.327 ms | **-25.7%** |

Those raids did not contain identical bots, routes, combat, weather, or entity visibility. A more conservative entity-matched comparison measured:

- Average FPS: **+14.9%**
- Median FPS: **+12.2%**
- 5% low FPS: **+30.8%**
- 1% low FPS: **+10.5%**
- Median main-thread time: **-10.9%**
- 95th-percentile main-thread time: **-23.2%**

In a live Streets A/B check, reported FPS moved from approximately 85 with Num4 OFF to 100-105 with the optimization stack ON, a roughly 17.6-23.5% observed increase at that location. Another lighter raid moved from approximately 110 to 130-140. These observations helped prove that at least part of the improvement was real rather than only a different raid, but they are not substitutes for perfectly synchronized replay benchmarking.

The suite did **not** eliminate all Streets drops. Heavy Streets combat still produced periods in the 40s, and VRAM usage approached the 12 GB capacity of the RTX 4070. The project should not be advertised as “double your Streets FPS.”

### Friend system: Intel Core i7-2600K + GTX 1070 Ti

The friend machine was the old-CPU target. Its retained Woods baseline resolved to approximately:

| Metric | Friend Woods capture |
|---|---:|
| Average FPS | 40.9 |
| Median FPS | 45.8 |
| 5% low FPS | 37.9 |
| 1% low FPS | 25.0 |
| Stable main-thread work | about 21.84 ms |
| GPU work | about 9.88 ms |

That means the GTX 1070 Ti was often waiting for the CPU. At the same scene workload, roughly 5.2 ms of CPU work had to be removed merely to reach a 16.67 ms/60 FPS frame budget.

The latest Interchange test on this machine was not profiled, but the player reported a noticeable improvement of approximately 10-15 FPS and was playing at the configured 60 FPS cap. This is the result originally hoped for on the i7-2600K. It is reported as a real user observation, not converted into invented CSV precision.

### What can honestly be claimed

- The suite produced a measurable improvement on the 5800X3D/RTX 4070 system.
- The conservative Customs result was about +15% average FPS with a much larger improvement in the 5% low.
- Same-raid A/B observations showed approximately +18-24% in the tested Streets location.
- The i7-2600K/GTX 1070 Ti user reported +10-15 FPS on Interchange and reached a 60 FPS cap.
- Stability and low-end behavior improved in several raids, but heavy combat and Streets can still drop sharply.
- Results are workload-dependent and should not be generalized into a promise of 30%, 50%, or 2x FPS for every user.

## Development history: 0.1 to 1.0

| Version | What changed and what was learned |
|---|---|
| 0.1.0 | Environment discovery, lifecycle, overlay, benchmark capture, report export, and disabled shadow experiment. The project began as diagnostics. |
| 0.1.1 | Added exact CPU/GPU/wait/GC milliseconds and moved hotkeys to the numpad to avoid F12 conflicts. |
| 0.2.0 | Added adaptive remote shadows, capture-only method timing, Fika observed-player targets, and optional offscreen skinning. |
| 0.3.0 | Added the first aggressive quality, forced-LOD, declutter, shadow/light/particle, and skinning experiments. Several LOD ideas were later removed. |
| 0.4.0 | Added the old-CPU preset, cached entity relevance, hidden-remote budgets, frame pacing, compact overlay, and the first periodic bot-counter scan fix. |
| 0.5.0 | Fixed the 460-490 ms friend stutter regression, removed forced LOD/dry runs, added continuous profiling, arms/body/IK budgeting, and log suppression. |
| 0.6.0 | Added complete Num4 A/B state recording, real-PiP resolution experimentation, complex-late budgeting, and full state restoration. |
| 0.7.0 | Quantified the i7-2600K frame budget, added processor-affinity restoration, direct Fika presentation hooks, audio timings, and installed-plugin frame timing. |
| 0.8.0 | Removed unsafe HDR/refresh overrides, added casing/flyby budgets, expanded world/ballistics/culling timing, and established that light Customs was CPU-main limited. |
| 0.9.0 | Analyzed Ground Zero/Streets, integrated baked PerfectCulling, added area-light caching and VRAM/memory counters, and reduced profiler self-interference. |
| 0.10.0 | Added the authority-aware sound-only remote shot path and culled hidden distant combat presentation without removing authoritative damage. |
| 0.11.0 | Replaced the small timing dump with cumulative 120-second profiles, Unity marker accumulation, Windows thread sampling, and broader installed-mod discovery. |
| 0.12.0 | Added the cached bot HUD, Dynamic Maps lean mode/image-preload prevention, ambient reflection budget, and additional capture-overhead reductions. |
| 0.12.1 | Restored useful map markers, removed hot per-character dictionaries, and added world culling/shadow/decal/weather presentation budgets. |
| 0.13.0 | Tested a full PiP camera stop. It proved the cost but left a frozen image and was not acceptable as a user solution. |
| 0.14.0 | Added named bosses/custom factions, font controls, fixed Dynamic Maps recenter limiting, and removed the broken PiP-off experiment from the UI. |
| 0.15.0 | Introduced separate client and SPT server loading accelerators with request/loading reports. |
| 0.15.1 | Fixed the early black-screen/headless deadlock created by the first synchronous-request approach. |
| 0.16.0 | Integrated PiP-Disabler, restored full PiP resolution, added headless/Fika/SAIN/ORBIT/BigBrain profiling, and added conservative authority pacing. |
| 0.16.1 | Directly targeted `ComplexLateUpdate`, `ArmsUpdate`, and `AmbientLight.LateUpdate`, the three largest methods in the relevant stress profile. |
| 0.16.2 | Parallelized safe loading preparation and loot serialization, added linear static-loot matching, and experimented with parallel scene preparation. |
| 0.16.3 | Fixed the Streets 53% loading deadlock by separating concurrent bundle preload from serial Unity scene creation/activation. |
| 1.0.0 | Production packaging, general presets, Extreme default, silent diagnostics, live config persistence, optional-dependency verification, documentation, licensing, and reproducible release checks. |

The detailed chronological record remains in [CHANGELOG.md](CHANGELOG.md).

## Included files and architecture

| Component | Installed location | Purpose |
|---|---|---|
| `TarkovPerformanceSuite.dll` | `BepInEx/plugins/TarkovPerformanceSuite` | Gameplay optimizations, optional compatibility, UI, profiler, and A/B switch. |
| `TarkovPerformance.Core.dll` | Same directory | Shared statistics, serialization, validation, safety, and scheduling primitives. |
| `TarkovPerformance.Loading.dll` | Same directory | Client/headless startup, profile, bundle, map, item, and loot-loading acceleration. |
| `TarkovPerformance.LoadingServer.dll` | `SPT/user/mods/TarkovPerformanceLoadingServer` | Optional SPT server worker/compression acceleration. |
| `PiP-Disabler.dll` and assets | `BepInEx/plugins/PiP-Disabler` | Optional complete no-PiP main-camera scope implementation. |
| BepInEx config files | `BepInEx/config` | Production defaults and F12-editable settings. |

## Requirements and compatibility

Version 1.0 was developed and built against:

- SPT 4.0.13
- EFT 0.16.9.40087
- Fika 2.3.3 during multiplayer/headless testing
- The BepInEx environment bundled with that SPT installation

Compatibility outside those versions is not guaranteed because EFT class/method layouts change.

| Software | Required? | Behavior when absent |
|---|---:|---|
| SPT/BepInEx client environment | Yes | The client DLLs cannot run without their host environment. |
| Fika | No | Offline SPT optimizations continue; Fika proxy/headless features are skipped. |
| Dynamic Maps | No | Map compatibility module reports unavailable and leaves maps untouched. |
| SAIN | No | Optional profiler targets are skipped. AI remains unchanged. |
| BigBrain | No | Optional profiler targets are skipped. |
| ORBIT | No | Optional profiler/headless navigation pacing is skipped. |
| Server accelerator | No | Client and headless DLLs still work. |
| PiP-Disabler | No | Full-resolution vanilla PiP remains. |
| BepInEx Configuration Manager | Recommended | F12 GUI is unavailable without it, but config files and hotkeys still work. |

The release verifier inspects compiled assembly references and confirms that the client/loading DLLs do not hard-link Fika, Dynamic Maps, SAIN, ORBIT, or BigBrain.

## Installation

### Normal offline SPT

1. Close EFT, the launcher, and the SPT server.
2. Download `TarkovPerformanceSuite-1.0.0.zip` from the GitHub release.
3. Extract it into the root SPT game folder so the included `BepInEx` and `SPT` directories merge with the existing directories.
4. Start SPT normally.

The server accelerator is optional even when the SPT server is on the same computer.

### Fika with a separate headless and server

1. Install the packaged `BepInEx` files on every playable EFT client.
2. Install the same `BepInEx` files on the Fika headless client.
3. Install `SPT/user/mods/TarkovPerformanceLoadingServer` once on the actual SPT server machine.
4. Do not expect the server component to work when it is installed only beside a client connected to a different server.

### Updating from a development version

1. Close every client/headless/server process.
2. Replace the old `BepInEx/plugins/TarkovPerformanceSuite` directory with the release version.
3. Extract the new config files or delete the old suite config to regenerate clean 1.0 defaults.
4. Old `OldCpuAggressive` and `StreetsExtreme` preset names were replaced by `Performance` and `Extreme`.

## First-launch behavior and presets

Version 1.0 is intentionally quiet:

- `Extreme` is selected.
- All behavior-changing optimization modules are enabled.
- Diagnostics overlay is hidden.
- Bot counter HUD is hidden.
- Continuous benchmark capture is off.
- Harmony method timing is off.
- Unity profiler recorders stay dormant.
- Client loading reports are off.
- SPT server reports are off.

| Preset | Intended use |
|---|---|
| Balanced | More conservative visual distances and update rates. |
| Performance | Full optimization stack with moderate thresholds. |
| Extreme | Strongest general-purpose production values; default for every map. |
| Custom | Automatically selected when an individual optimization value is edited. |

Gameplay config changes are saved immediately through BepInEx and applied in the running game. A test changes a config value, saves it, creates a fresh config instance, and verifies that the changed value reloads. Loading/server settings are read during process startup and require a restart.

## Controls

| Key | Action |
|---|---|
| Num4 | Toggle all behavior-changing gameplay optimizations for an in-raid A/B comparison. |
| Num3 | Switch between PiP-Disabler's main-camera scope mode and full-resolution vanilla PiP. |
| Num7 | Show/hide the diagnostics overlay. This also activates/deactivates the lightweight Unity marker recorders. |
| Num8 | Start/stop continuous 120-second benchmark captures. Each completed capture exports and the next begins automatically. |
| Num9 | Export an immediate diagnostic/profile report. |
| F12 | Open BepInEx Configuration Manager and search for “Tarkov Performance Suite.” |

Num5 and Num6 remain advanced experiment toggles for offscreen skinning and remote shadows when assigned/enabled in configuration.

## How to perform a useful A/B test

1. Use a repeatable route and avoid comparing the empty start of one raid with heavy combat in another.
2. Enable Num8 if you want saved CSV data.
3. Let the raid settle for at least several seconds.
4. Record 45-120 seconds with Num4 ON.
5. Press Num4, wait at least five seconds for transients, and record a comparable area with Num4 OFF.
6. Repeat the switch more than once when possible.
7. Compare main-thread milliseconds, GPU milliseconds, entity visibility, draw calls, SetPass calls, and the low-end FPS—not only average FPS.
8. Keep resolution, graphics settings, scope, weather, route, server/headless configuration, and other mods unchanged.

The benchmark records the Num4 state on every sample so mixed captures can be separated later.

## Diagnostic output

When explicitly enabled, output is written beneath:

`BepInEx/plugins/TarkovPerformanceSuite/`

- `benchmarks/`: per-frame CSV and optional JSON.
- `diagnostics/`: environment, installed plugin, feature, exception, and current metric summaries.
- `profiles/`: cumulative managed method and process-thread CPU profiles.
- `loading-reports/`: client/headless loading stages and slow requests when loading reports are enabled.
- `AVAILABLE_PROFILER_METRICS.runtime.md`: Unity markers exposed by the current EFT player build.

The cumulative CPU profile reports:

- Self milliseconds: time attributed to the method excluding instrumented children.
- Inclusive milliseconds: time including instrumented child calls; do not add nested inclusive rows together.
- Calls, average, approximate p95, and maximum.
- Main-thread versus worker-thread execution.
- Percentage of one full CPU core over the capture.
- Unity frame-marker accumulation and OS process/thread CPU time.

Native engine/driver costs cannot be fully assigned to managed C# methods, which is why both Harmony method timing and Unity/OS frame counters are necessary.

## Validation performed for 1.0

- Release client/core/loading build: zero warnings and zero errors.
- SPT server plugin build: zero warnings and zero errors.
- Automated checks: 13/13 passed.
- BepInEx config save/reload persistence test passed.
- Production ZIP layout and required-file verification passed.
- Optional assembly dependency audit passed.
- Silent-default config audit passed.
- Third-party PiP license and assets included.
- Release ZIP SHA-256: `1A4FE8408D2A21B48737D8367ACF6113992D136214A12553A7AB5E36E242CB39`.

Game testing remains more important than unit tests for Unity/EFT behavior. Passing these checks does not prove compatibility with every mod list.

## Known risks and limitations

- This is AI-generated experimental code. It needs expert review.
- Harmony targets depend on EFT/SPT versions and may fail after an update.
- Optional integrations intentionally fail open, which can mean an optimization silently becomes unavailable after another mod changes its internals.
- Extreme reduces some visual presentation quality, shadow range, lighting frequency, particles, and distant effects.
- Sound-only remote combat is deliberately restricted to non-host Fika clients with baked occlusion and trajectory safety. Bugs in visibility/mod interactions remain possible.
- The loading accelerator touches sensitive startup and map-loading paths. Earlier deadlocks were found and fixed through logs, but new SPT/EFT versions may change those assumptions.
- The server's faster compression trades network size for CPU time. This is suitable for typical local/LAN SPT use but is not universally optimal.
- Profiling itself has overhead. Version 1.0 keeps it completely opt-in for normal users.
- No telemetry, external analytics, automatic updater, or background network service is included. The server/client communicate only through the normal SPT/Fika environment.

## Uninstall

Remove:

- `BepInEx/plugins/TarkovPerformanceSuite`
- `BepInEx/plugins/PiP-Disabler` if it was installed only for this suite
- `BepInEx/config/com.lucaswilluweit.tarkovperformancesuite.cfg`
- `BepInEx/config/com.lucaswilluweit.tarkovperformancesuite.loading.cfg`
- `BepInEx/config/com.fiodor.pipdisabler.cfg` if PiP-Disabler is also removed
- `SPT/user/mods/TarkovPerformanceLoadingServer`

The suite restores runtime state on disable, raid end, and normal plugin shutdown, but closing EFT before uninstalling is recommended.

## Source, building, and review

The repository does not contain Battlestate Games or SPT binaries. Build references must come from a local SPT installation.

```powershell
$env:SPT_REFERENCE_ROOT = 'D:\path\to\SPT'
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -ReferenceRoot $env:SPT_REFERENCE_ROOT -Configuration Release
powershell -ExecutionPolicy Bypass -File .\scripts\package-friend.ps1 -ReferenceRoot $env:SPT_REFERENCE_ROOT -Configuration Release
powershell -ExecutionPolicy Bypass -File .\scripts\verify-release.ps1 -ReferenceRoot $env:SPT_REFERENCE_ROOT
```

Areas where experienced contributors would be especially useful:

- Review Harmony targets and fail-open behavior against current SPT/EFT source mappings.
- Audit threading assumptions in loading and Fika loot serialization.
- Reproduce benchmarks with controlled routes, fixed bot seeds, and automated frametime capture.
- Review the combat trajectory/visibility safety conditions.
- Replace reflection-based optional integration with maintained public APIs where available.
- Add CI that can validate against legally supplied external reference assemblies.
- Test additional CPUs, especially older 4-core/8-thread systems, and additional GPUs/VRAM capacities.
- Split experimental features from conservative production-safe modules.

Issues and pull requests are welcome. A qualified maintainer may fork or take over the project under the MIT license as long as third-party attribution is preserved.

## Credits and license

- Project direction, hardware testing, gameplay validation, and release: Lucas Willuweit.
- Suite implementation and documentation: generated through OpenAI Codex under Lucas's direction and testing.
- PiP-Disabler 1.5.0: Fiodorwellfme, included unmodified under the MIT license: <https://github.com/Fiodorwellfme/PiP-Disabler>.
- SPT, Fika, Dynamic Maps, SAIN, BigBrain, ORBIT, and Battlestate Games are separate projects and are not distributed as source dependencies by this repository.

Tarkov Performance Suite is released under the [MIT License](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for bundled third-party licensing.
