# Uncertainties and safe fallbacks

- Version 0.8.0 builds and passes local automated tests but still requires live validation of the new distant-casing/flyby budgets, Num4 restoration, and the corrected vanilla-rate/HDR PiP path. The prior manual optic-render path has been removed.
- Fika's installed DLL has no reliable target-framework attribute. It is a soft runtime dependency and is never referenced by the core assembly.
- Obfuscated EFT lifecycle methods were not guessed. Raid lifecycle uses the verified `GameWorld` singleton/list with time-based polling, and treats destruction/replacement as raid end/start.
- Only players classified from verified local, bot-owner, or Fika observed-AI signals are eligible for either renderer experiment. Anything uncertain remains unchanged.
- `SkinnedMeshRenderer.updateWhenOffscreen=false` can expose incorrect imported bounds on unusual animation/equipment. Fika's own player-visibility state is checked in addition to Unity renderer visibility, and it restores on visibility/proximity/Num4 OFF/raid end. It is enabled in the aggressive test profile, so any frozen or missing equipment is an immediate rollback signal.
- PiP scopes keep Tarkov's vanilla camera cadence and HDR state. Only normal-optic render resolution and optional MSAA are changed; thermal/NVG optics bypass both. A scope that turns black or loses its reticle is a failure; Num4 OFF must restore vanilla resolution immediately.
- Exposing all logical processors does not parallelize EFT's main thread. It only removes an affinity restriction if one exists, allowing Unity jobs and compatible mod work to use all available logical processors. Hyper-Threading can help, do nothing, or regress a particular machine; Num4 restores the exact original mask for measurement.
- The adaptive shadow controller responds to sustained measured CPU-main time, not isolated hitch frames. Its actual gain is unproven until baseline and shadow-enabled captures are compared.
- Global particle/audio/corpse-rigidbody counts are intentionally reported unavailable rather than introducing full-scene scans into the profiler.
- Runtime profiler marker names and current patch owners can only be known inside the player. The plugin enumerates them and exports the results; documentation does not claim static values.
