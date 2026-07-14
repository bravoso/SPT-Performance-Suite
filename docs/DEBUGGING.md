# Debugging

Start with `BepInEx/LogOutput.log`. Search for `Tarkov Performance Suite`, environment versions, raid start/end, patch verification, benchmark exports, and circuit breakers.

Press F10 to write a self-contained diagnostic report under `BepInEx/plugins/TarkovPerformanceSuite/diagnostics`. It contains loaded plugins/patchers, live Harmony owners, available counters, feature/entity state, recent exceptions, fingerprints, and suite overhead.

Release and Debug builds both emit portable PDB files. For an advanced manual session, attach Visual Studio or Rider to `EscapeFromTarkov.exe` in the disposable installation. ILSpy is suitable for read-only inspection. dnSpyEx may be used manually only on the disposable installation.

This project does not replace `mono.dll`, rewrite `Assembly-CSharp.dll`, enable a Unity debug player, or install a prepatcher. Any such future diagnostic change requires separate justification and deliberate manual approval.

