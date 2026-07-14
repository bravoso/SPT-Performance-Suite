# Uncertainties and safe fallbacks

- Version 0.1.1 loaded and captured successfully in the user's active SPT installation. Version 0.2.0 still requires its own in-game validation, especially the new Fika timing targets and offscreen-skinning restoration.
- Fika's installed DLL has no reliable target-framework attribute. It is a soft runtime dependency and is never referenced by the core assembly.
- Obfuscated EFT lifecycle methods were not guessed. Raid lifecycle uses the verified `GameWorld` singleton/list with time-based polling, and treats destruction/replacement as raid end/start.
- Only players classified from verified local, bot-owner, or Fika observed-AI signals are eligible for either renderer experiment. Anything uncertain remains unchanged.
- `SkinnedMeshRenderer.updateWhenOffscreen=false` can expose incorrect imported bounds on unusual animation/equipment. Fika's own player-visibility state is checked in addition to Unity renderer visibility, the feature waits 0.5 seconds, and it restores on visibility/proximity/disable/raid end; nevertheless it remains disabled by default until scope and combat testing passes.
- The adaptive shadow controller responds to sustained measured CPU-main time, not isolated hitch frames. Its actual gain is unproven until baseline and shadow-enabled captures are compared.
- Global particle/audio/corpse-rigidbody counts are intentionally reported unavailable rather than introducing full-scene scans into the profiler.
- Runtime profiler marker names and current patch owners can only be known inside the player. The plugin enumerates them and exports the results; documentation does not claim static values.
