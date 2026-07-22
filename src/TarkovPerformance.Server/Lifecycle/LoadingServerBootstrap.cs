using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;

namespace TarkovPerformanceSuite.Server.Lifecycle;

/// <summary>
/// Starts the optional server instrumentation before regular SPT mods are loaded.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public sealed class LoadingServerBootstrap(ISptLogger<LoadingServerBootstrap> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.Initialize(logger);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records when SPT has completed loading its database and post-database mods.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public sealed class DatabaseReadyProbe(ISptLogger<DatabaseReadyProbe> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.MarkDatabaseReady(logger);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records the final server-ready checkpoint after the regular mod loader has completed.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 2)]
public sealed class ServerReadyProbe(ISptLogger<ServerReadyProbe> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.MarkServerReady(logger);
        return Task.CompletedTask;
    }
}
