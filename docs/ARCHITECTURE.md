# Architecture

The initial prototype deliberately combines diagnostics, runtime features, and the soft Fika adapter in one BepInEx DLL while keeping separate folders and namespaces. Pure, unit-testable logic is a separate assembly.

```text
TarkovPerformanceSuite/
|-- src/
|   |-- TarkovPerformance.Core/          pure logic, no Unity/EFT dependency
|   `-- TarkovPerformance.Plugin/        one BepInEx DLL
|       |-- Configuration/
|       |-- Core/                        lifecycle and runtime information
|       |-- Diagnostics/                 overlay, recorders, Harmony timing
|       |-- Features/                    entity registry and shadow experiment
|       `-- FikaAdapter/                 cached reflection; no Fika reference
|-- tests/TarkovPerformance.Tests/       dependency-free automated runner
|-- scripts/                             discovery, build, deploy, rollback
|-- docs/
|-- config/
|-- benchmarks/                          reserved for supplied comparison data
|-- README.md
|-- CHANGELOG.md
`-- TarkovPerformanceSuite.sln
```

`TarkovPerformance.Core.dll` and `TarkovPerformanceSuite.dll` are deployed together. No prepatcher exists in 0.1.0.

Main-thread boundaries are explicit: Unity/EFT objects are read or changed only from the plugin's Unity callbacks. Only immutable benchmark sample arrays are passed to a worker for file serialization.

