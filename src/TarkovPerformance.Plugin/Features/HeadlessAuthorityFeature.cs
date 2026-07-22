using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Compatibility tombstone for the removed Fika/ORBIT headless pacing experiment.</summary>
/// <remarks>
/// The former implementation changed Fika bot snapshot cadence and ORBIT navigation batch
/// fields through reflection. Those behaviors were not independently validated with bot combat,
/// so the implementation and its Harmony patches were deleted in 1.0.1. This class only keeps
/// existing configuration and UI wiring source-compatible while forcing the feature off.
/// </remarks>
internal sealed class HeadlessAuthorityFeature : IPerformanceFeature
{
    private const string RetirementReason = "removed pending independent bot-combat validation; Fika and ORBIT keep their original timing";

    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private bool _retirementLogged;

    internal HeadlessAuthorityFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _ = exceptions;
    }

    public string Name => "Headless Authority Pacing";

    public bool IsAvailable => false;

    public bool IsEnabled => false;

    internal string StatusText => RetirementReason;

    public void Initialize()
    {
        DisableLegacySetting();
        LogRetirementOnce();
    }

    public void SetEnabled(bool enabled)
    {
        _ = enabled;
        DisableLegacySetting();
        LogRetirementOnce();
    }

    public void OnRaidStarted()
    {
        DisableLegacySetting();
    }

    public void OnRaidEnded() { }

    public void Shutdown() { }

    internal void Tick(float now)
    {
        _ = now;
    }

    private void DisableLegacySetting()
    {
        _configuration.HeadlessAuthorityEnabled.Value = false;
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
