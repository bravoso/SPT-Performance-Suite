# Third-party notices

## CompoundingPerf (independent prior art; not bundled)

[CompoundingPerf](https://github.com/EchoStarz/CompoundingPerf) by EchoStarz independently changes SPT HTTP response compression from `CompressionLevel.SmallestSize` to a faster compression level. Its public implementation predates Tarkov Performance Suite's server-loading component.

Tarkov Performance Suite does not include CompoundingPerf's source files, WebSocket changes, dependency-injection subclasses, compiled binary, or method bodies. A repository-wide source comparison performed on 2026-07-15 did not find a substantial matching code block. Both projects necessarily reproduce the small public response contract of SPT's `SptHttpListener.SendZlibJson`: status code, JSON content type, session cookie, zlib stream, and UTF-8 response bytes. The suite implements that contract as a Harmony prefix; CompoundingPerf implements it through a different listener/patch design.

Do not enable both fast-compression patches at the same time. They target the same SPT method, so Harmony patch order can determine which replacement runs. This notice credits the earlier implementation and makes the overlap explicit; no CompoundingPerf code is relicensed or redistributed here. CompoundingPerf is MIT licensed.

## PiP-Disabler 1.5.0

Tarkov Performance Suite packages the unmodified PiP-Disabler implementation by Fiodorwellfme to provide a complete non-PiP scope mode. It replaces the secondary optic render with main-camera FOV zoom, reticle rendering, lens masking, and scope-housing mesh handling. Source: https://github.com/Fiodorwellfme/PiP-Disabler

PiP-Disabler is licensed under the MIT License. Its license is included beside the packaged plugin as `LICENSE-PiP-Disabler.txt`.

## Tyrian-DeClutterer (research and prior art; not bundled)

[Tyrian-DeClutterer](https://github.com/Xenoxia8953/Tyrian-DeClutterer) by Xenoxia8953 was examined as prior art for scene decluttering. Its high-level approach and categories such as garbage, litter, decals, puddles, spent casings, and shards informed the scope of Tarkov Performance Suite's cosmetic-clutter experiment.

No Tyrian-DeClutterer source file, compiled binary, method body, object-disabling routine, or patch class is included in Tarkov Performance Suite. The suite's implementation was written separately and uses a different design: it incrementally classifies individual Unity renderers, sets `Renderer.forceRenderingOff`, preserves the original renderer state, and restores every change when disabled or when the raid ends. It does not use Tyrian-DeClutterer's whole-`GameObject` deactivation implementation.

Tyrian-DeClutterer did not publish a license file in its GitHub repository when this notice was written. Accordingly, this project does not claim to relicense or redistribute any Tyrian-DeClutterer code. This acknowledgement credits the prior art and design influence only.
