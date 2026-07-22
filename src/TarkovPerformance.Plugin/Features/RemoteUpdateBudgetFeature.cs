using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Preserves the historical remote-budget columns in diagnostic reports.</summary>
/// <remarks>
/// These values remain zero because the corresponding optimization was removed. Keeping the
/// schema stable allows old and new benchmark files to be compared by the existing tools.
/// </remarks>
internal readonly struct RemoteUpdateBudgetCounters
{
    internal int RemoteCharacters => 0;
    internal int HiddenCharacters => 0;
    internal int BakedHiddenCharacters => 0;
    internal int BudgetedCharacters => 0;
    internal int VisibleDistantCharacters => 0;
    internal int FrozenHiddenCharacters => 0;
    internal int CulledAnimators => 0;
    internal long SkippedPropUpdates => 0;
    internal long SkippedTriggerSearches => 0;
    internal long SkippedPresentationUpdates => 0;
    internal double AverageMs => 0;
}

/// <summary>Compatibility tombstone for the removed remote-player update experiment.</summary>
/// <remarks>
/// Version 0.16 introduced prefixes that could skip observed-player arms, body, IK, and
/// <c>ComplexLateUpdate</c> calls. A headless classification failure could apply those prefixes
/// to an authoritative bot and interfere with weapon-state progression. The implementation and
/// every Harmony target were deleted in 1.0.1. This tombstone only forces legacy configuration
/// values off and explains the retirement in the UI and log.
/// </remarks>
internal sealed class RemoteUpdateBudgetFeature : IPerformanceFeature
{
    private const string RetirementReason = "removed for combat safety; vanilla Player arms, body, IK, and ComplexLateUpdate always run";

    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private bool _retirementLogged;

    internal RemoteUpdateBudgetFeature(
        ManualLogSource logger,
        PluginConfiguration configuration,
        EntityRegistry registry,
        RecentExceptionLog exceptions
    )
    {
        _logger = logger;
        _configuration = configuration;
        _ = registry;
        _ = exceptions;
    }

    public string Name => "Remote Character CPU Budget";

    public bool IsAvailable => false;

    public bool IsEnabled => false;

    internal RemoteUpdateBudgetCounters Counters => default;

    internal string StatusText => RetirementReason;

    public void Initialize()
    {
        DisableLegacySettings();
        LogRetirementOnce();
    }

    public void SetEnabled(bool enabled)
    {
        _ = enabled;
        DisableLegacySettings();
        LogRetirementOnce();
    }

    public void OnRaidStarted()
    {
        DisableLegacySettings();
    }

    public void OnRaidEnded() { }

    public void Shutdown() { }

    internal void Tick(float now)
    {
        _ = now;
    }

    private void DisableLegacySettings()
    {
        _configuration.RemoteUpdateBudgetEnabled.Value = false;
        _configuration.RemoteAnimatorCullingEnabled.Value = false;
        _configuration.RemotePresentationBudgetEnabled.Value = false;
        _configuration.RemoteComplexLateUpdateBudgetEnabled.Value = false;
        _configuration.RemoteFreezeHiddenPresentation.Value = false;
    }

    private void LogRetirementOnce()
    {
        if (_retirementLogged)
        {
            return;
        }

        _retirementLogged = true;
        _logger.LogWarning(Name + " " + RetirementReason + ".");
    }
}
