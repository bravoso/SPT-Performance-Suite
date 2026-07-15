# Third-party notices

## PiP-Disabler 1.5.0

Tarkov Performance Suite packages the unmodified PiP-Disabler implementation by Fiodorwellfme to provide a complete non-PiP scope mode. It replaces the secondary optic render with main-camera FOV zoom, reticle rendering, lens masking, and scope-housing mesh handling. Source: https://github.com/Fiodorwellfme/PiP-Disabler

PiP-Disabler is licensed under the MIT License. Its license is included beside the packaged plugin as `LICENSE-PiP-Disabler.txt`.

## Tyrian-DeClutterer (research and prior art; not bundled)

[Tyrian-DeClutterer](https://github.com/Xenoxia8953/Tyrian-DeClutterer) by Xenoxia8953 was examined as prior art for scene decluttering. Its high-level approach and categories such as garbage, litter, decals, puddles, spent casings, and shards informed the scope of Tarkov Performance Suite's cosmetic-clutter experiment.

No Tyrian-DeClutterer source file, compiled binary, method body, object-disabling routine, or patch class is included in Tarkov Performance Suite. The suite's implementation was written separately and uses a different design: it incrementally classifies individual Unity renderers, sets `Renderer.forceRenderingOff`, preserves the original renderer state, and restores every change when disabled or when the raid ends. It does not use Tyrian-DeClutterer's whole-`GameObject` deactivation implementation.

Tyrian-DeClutterer did not publish a license file in its GitHub repository when this notice was written. Accordingly, this project does not claim to relicense or redistribute any Tyrian-DeClutterer code. This acknowledgement credits the prior art and design influence only.
