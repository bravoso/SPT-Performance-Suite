# Uncertainties and safe fallbacks

- No disposable `SPT_TEST_ROOT` exists, so plugin loading, runtime profiler marker availability, live Harmony owners, raid transitions, and renderer restoration cannot yet be verified in game.
- Fika's installed DLL has no reliable target-framework attribute. It is a soft runtime dependency and is never referenced by the core assembly.
- Obfuscated EFT lifecycle methods were not guessed. Raid lifecycle uses the verified `GameWorld` singleton/list with time-based polling, and treats destruction/replacement as raid end/start.
- Only players classified from verified local, bot-owner, or Fika observed-AI signals are eligible for the shadow experiment. Anything uncertain remains unchanged.
- Global particle/audio/corpse-rigidbody counts are intentionally reported unavailable rather than introducing full-scene scans into the profiler.
- Runtime profiler marker names and current patch owners can only be known inside the player. The plugin enumerates them and exports the results; documentation does not claim static values.

