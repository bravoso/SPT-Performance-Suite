# Tarkov Performance Suite 1.0

## Short description

Tarkov Performance Suite makes the client spend less CPU time on things you cannot currently see. It reduces repeated work for hidden/distant characters, shadows, ambient lights, particles, decals, weather, bullet effects, shell physics, Dynamic Maps, and known mod hot paths. That gives the CPU more time to prepare frames so an underused GPU can render more often.

It also includes separate loading accelerators for the client/headless and SPT server. They use more available workers for safe read-only preparation, overlap bundle I/O in bounded batches, replace a slow nested loot lookup, and use faster server response compression. Unity scene creation, GameObjects, physics, AI, damage, inventory, and network authority are not moved to unsafe worker threads.

## What it changes

- Hidden distant Fika proxy arms, body, IK, and late visual presentation update less often or stop until visible again.
- Distant hidden gunshots keep sound and authoritative damage while invisible muzzle flashes, lights, impacts, decals, and casing work can be skipped when strict safety checks pass.
- Unchanged area-light commands are reused; ambient reflections and expensive ambient/world visual maintenance run at controlled rates.
- Distant shadows, offscreen skinning, particle raycasts, pixel lights, and cosmetic clutter are budgeted.
- Dynamic Maps keeps the player, party, quests, friendly kills, extracts, doors, transits, and backpacks while removing live enemy markers and unnecessary all-map image preloading.
- Known periodic scene scans and hot projectile info logs are replaced or suppressed only for recognized compatible mods.
- Real PiP stays full resolution, or Num3 selects PiP-Disabler's complete main-camera scope replacement.
- The optional profiler can rank cumulative milliseconds by method over a capture, but all profiling and debug UI are off by default.

## Default experience

The `Extreme` profile is enabled on first install. It is the strongest general profile and works on every map. No diagnostics window or bot counter opens automatically. Available F12 profiles are `Balanced`, `Performance`, `Extreme`, and `Custom`. Config changes save immediately and gameplay values update during the running game. Loading/server configuration changes require a restart.

## Performance observed during development

These are measurements, not promises. A conservative entity-matched Customs comparison measured:

- Average FPS: +14.9%
- 5% low: +30.8%
- 1% low: +10.5%
- Median main-thread time: -10.9%
- 95th-percentile main-thread time: -23.2%

The raw first-to-latest Customs captures measured +26.0% average FPS, +35.0% 5% low, and +27.8% 1% low. A same-raid Streets A/B observation moved from about 85 FPS with the stack disabled to 100-105 enabled. Results vary with bots, combat, route, weather, hardware, Fika authority placement, and installed mods. It will not double FPS in every raid, and a GPU/VRAM limit cannot be fixed by removing CPU work.

## Requirements and optional integrations

Required:

- SPT 4.0.x with its bundled BepInEx client environment. Development and release verification targeted SPT 4.0.13 / EFT 0.16.9.40087.

Optional:

- Fika: enables observed-player and headless authority integrations. Without Fika, normal offline SPT optimizations still work.
- Dynamic Maps: its compatibility budget activates only when the mod is installed.
- SAIN and BigBrain: optional profiler targets only; absence is harmless.
- ORBIT: optional profiler target and bounded headless navigation pacing; absence is harmless.
- Server accelerator: optional. Install it only on the real SPT server process.

There are no hard assembly dependencies on Fika, Dynamic Maps, SAIN, ORBIT, or BigBrain. Every optional integration is found by name/reflection and fails open, so a missing or changed optional mod leaves that mod untouched instead of preventing EFT from booting.

## Fika installation

- Playable client: install the `BepInEx` files.
- Headless client: install the same `BepInEx` files so loading and authority-side features can activate.
- Real SPT server: install `SPT\user\mods\TarkovPerformanceLoadingServer` once.

For non-Fika SPT, install the `BepInEx` files on the game and optionally the server folder on the local SPT server.

## Hotkeys

- Num4: all gameplay optimizations on/off.
- Num3: no-PiP replacement/full-resolution vanilla PiP.
- Num7: diagnostics overlay.
- Num8: repeating profiler capture.
- Num9: immediate report.
- F12: all configuration.

## Credits and license

Tarkov Performance Suite is MIT licensed. The release packages the unmodified [PiP-Disabler 1.5.0](https://github.com/Fiodorwellfme/PiP-Disabler) by Fiodorwellfme under its MIT license. No source code from Dynamic Maps, Fika, SAIN, ORBIT, BigBrain, or Tyrian De-Clutterer is included. Their names are used only for optional runtime compatibility.
