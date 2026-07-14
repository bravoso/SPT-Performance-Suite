# Roadmap

Version 0.8.0 implements the central relevance cache, an aggressive hidden-remote presentation budget, and bounded client-only combat cosmetics. It never throttles packets, root snapshot application, visibility, AI authority, combat, ballistics, damage, inventory, or navigation. Hidden prop, trigger-search, arms, body, IK, and outer late-presentation work is amortized and full-rate presentation resumes as soon as the existing culling signal reports visibility. PiP remains a real second camera at vanilla refresh and HDR.

## Stage A - Isolation experiments

Continue independently benchmarking remote shadows, offscreen skinning, Animator, IK, procedural weapon animation, audio/surface checks, Fika `ManualStateUpdate`, exact Fika observed-player late/visual methods, corpse rigidbodies, particles/transients, global player-manager iteration, render submission/culling, and garbage hot paths.

## Stage B - Presentation LOD

Potential relevance tiers: full nearby/local updates; reduced expensive visible-distant presentation; lower-frequency hidden-distant-combat presentation while immediate events remain intact; very-low-frequency hidden-peaceful presentation with smooth roots. Gameplay authority is never throttled.

## Stage C - Central relevance cache

Share distance, visibility/recency, camera/scope/combat relevance, deadlines, and shadow/animation/audio states instead of duplicating checks.

## Stage D - Pure-data asynchronous processing

Only copied immutable data may be used for distance classes, spatial indexing, priorities, visibility candidates, interpolation math, and changed-state detection. Unity/EFT/Fika objects remain on the main thread.

## Stage E - Batched raycasts

Consider `RaycastCommand` only after API and profiling proof, and only for presentation relevance—not AI vision or ballistics.

## Stage F - Fika adapter

After measurement, consider redundant-write reduction or cheap hidden-AI presentation while preserving packet processing, root sync, weapons, damage, death, interactions, and immediate promotion. Prefer a standalone adapter over a fork.

## Stage G - Base EFT global loops

Only proven hot loops may receive cached lookups, reused buffers, identical-write avoidance, reduced polling, LINQ removal, or amortized scans.

## Stage H - Corpse/transient optimizer

Potentially sleep settled corpse rigidbodies, disable distant corpse shadows, and pool/cap safe transient effects while preserving loot/collision.

## Stage I - Optional preloader patcher

Only if Harmony cannot safely implement a measured optimization. It must be a separate exact-version/fingerprint-gated DLL, refuse unknown versions, never modify enums, and never overwrite the installed assembly.

## Stage J - Experimental GPU rendering

Only if profiling proves render submission dominant: limited identical static decorative meshes, reversible original renderers, preserved colliders, main/scope camera support, disabled by default. Never replace EFT's complete renderer.
