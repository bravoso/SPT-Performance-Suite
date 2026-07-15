using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class HotPathLogSuppressionFeature : IPerformanceFeature
    {
        private static HotPathLogSuppressionFeature _instance;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private Harmony _harmony;
        private MethodInfo _target;
        private bool _raidActive;
        private long _suppressed;

        internal HotPathLogSuppressionFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "Known Combat Log Suppression";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal long Suppressed => _suppressed;
        internal string StatusText => IsEnabled
            ? "enabled | exact RealisticFrag projectile logs suppressed " + _suppressed
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            _instance = this;
            try
            {
                _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.hot-path-logs");
                _target = AccessTools.Method(typeof(ManualLogSource), nameof(ManualLogSource.Log), new[] { typeof(LogLevel), typeof(object) });
                MethodInfo prefix = AccessTools.Method(typeof(HotPathLogSuppressionFeature), nameof(LogPrefix));
                if (_target == null || prefix == null) throw new MissingMethodException("BepInEx ManualLogSource.Log was not found.");
                _harmony.Patch(_target, prefix: new HarmonyMethod(prefix));
                _breaker.Success();
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name + " patch install", ex);
                _breaker.Failure();
                _configuration.HotPathLogSuppressionEnabled.Value = false;
                _logger.LogWarning(Name + " unavailable: " + ex.Message);
            }
            SetEnabled(_configuration.HotPathLogSuppressionEnabled.Value);
        }

        public void OnRaidStarted() { _raidActive = true; _suppressed = 0; }
        public void OnRaidEnded() { _raidActive = false; }

        public void SetEnabled(bool enabled)
        {
            if (enabled && (_breaker.IsOpen || _target == null)) return;
            IsEnabled = enabled;
            _configuration.HotPathLogSuppressionEnabled.Value = enabled;
        }

        public void Shutdown()
        {
            _raidActive = false;
            IsEnabled = false;
            if (_target != null && _harmony != null) _harmony.Unpatch(_target, HarmonyPatchType.Prefix, _harmony.Id);
            _target = null;
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private bool ShouldLog(ManualLogSource source, LogLevel level, object data)
        {
            if (!IsEnabled || !_raidActive || source == null) return true;
            if ((level & (LogLevel.Fatal | LogLevel.Error | LogLevel.Warning)) != 0) return true;
            if (!string.Equals(source.SourceName, "RealisticFrag.Client", StringComparison.Ordinal)) return true;
            if (!(data is string message) || !message.StartsWith("[RealisticFrag] gated frag for ammo ", StringComparison.Ordinal)) return true;
            _suppressed++;
            return false;
        }

        private static bool LogPrefix(ManualLogSource __instance, LogLevel level, object data)
            => _instance == null || _instance.ShouldLog(__instance, level, data);
    }
}
