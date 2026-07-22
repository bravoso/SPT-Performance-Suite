# Contributing and maintainer handoff

This repository is an AI-generated experimental project being prepared for human review. A passing build is necessary but not sufficient: changes to Harmony patches must be justified against EFT/SPT/Fika behavior and tested in the process that owns the patched method.

## Code style

The repository follows the current SPT server conventions where they apply:

- four spaces, LF line endings, and a final newline;
- braces for every control-flow body, including one-line bodies;
- file-scoped namespaces for new files;
- `System` using directives first and all using directives outside the namespace;
- `_camelCase` private/internal fields and `PascalCase` constants;
- C# keywords such as `string` and `int` instead of `String` and `Int32`;
- no expression-bodied members except short lambdas;
- a 140-column target;
- CSharpier as the final mechanical formatter.

Run:

```powershell
.\.tools\csharpier\csharpier.exe format src tests --no-cache
```

The checked-in `.editorconfig` is the source of style intent. CSharpier's output is the source of layout truth.

## Comment policy

Comments explain ownership, safety assumptions, and reasons. Do not narrate obvious assignments. Every subsystem and non-trivial helper should state:

- what process owns it;
- whether it touches gameplay authority or presentation only;
- why suppressing an original EFT method is safe;
- what happens when a dependency or target is missing;
- where original state is stored and when it is restored;
- which report or profile justified the optimization.

If those questions cannot be answered, the patch is not ready to be enabled by default.

## Build and automated tests

Set a read-only reference installation and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -ReferenceRoot "D:\SPT" -Configuration Release
```

The server component targets .NET 9 and is built separately with `scripts/build-server.ps1`. Never deploy tests into the only working SPT installation; deployment scripts require a separate disposable test root.

The dependency-free tests currently cover rolling statistics, serialization, classification, validation, adaptive budgets, rollback caches, compatibility helpers, and BepInEx config persistence. Runtime Harmony behavior still needs an integration-test harness.

## Patch review checklist

- [ ] Exact target type and signature are documented.
- [ ] Supported EFT/SPT/Fika versions are stated.
- [ ] Existing Harmony owners are inspected.
- [ ] Missing or changed targets fail open.
- [ ] Local players and protected entities cannot enter a remote budget path.
- [ ] Offline-authoritative bot AI is not mistaken for remote presentation.
- [ ] Every durable mutation has an exact rollback path.
- [ ] Exceptions cannot flood a per-frame log.
- [ ] The enabled and disabled states can be compared in the same raid.
- [ ] Counters prove the patch executed.
- [ ] Bot combat and visual correctness were checked, not only FPS.

## Performance evidence

Submit the raw report pair, not only screenshots or average FPS. A useful comparison includes the same map, route, weather/time, bot setup, Fika role, graphics settings, warm/cold cache state, capture duration, and toggled suite state. Report FPS mean, 1% and 0.1% lows, frame-time percentiles, and feature counters. Random raids are exploratory evidence because AI population and combat are not controlled.

## Provenance and licensing

Before adapting another project, record its repository, author, license, exact files or concepts used, and whether code is copied, modified, independently implemented, or only prior art. Preserve required notices in `THIRD_PARTY_NOTICES.md`. If provenance is uncertain, stop publication of the affected component until a human review resolves it.

The Fika adapter in this repository uses reflection and does not compile or package Fika source. That statement should be re-audited whenever Fika-specific patches change.
