using System;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>
/// Rate-limits global visual maintenance identified by the cumulative Streets profile.
/// None of these callbacks owns simulation, networking, damage, audio, or input.
/// </summary>
internal sealed class WorldPresentationRateFeature : IPerformanceFeature
{
    private static WorldPresentationRateFeature _instance;
    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly RecentExceptionLog _exceptions;
    private Harmony _harmony;
    private MethodInfo _cullingUpdate;
    private MethodInfo _distantShadowUpdate;
    private MethodInfo _decalUpdate;
    private MethodInfo _weatherUpdate;
    private bool _raidActive;
    private bool _patchesInstalled;
    private float _nextCulling;
    private float _nextDistantShadow;
    private float _nextDecal;
    private float _nextWeather;
    private long _skippedCulling;
    private long _skippedDistantShadow;
    private long _skippedDecal;
    private long _skippedWeather;

    internal WorldPresentationRateFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "World Presentation CPU Budget"; }
    }

    public bool IsAvailable
    {
        get { return _patchesInstalled; }
    }

    public bool IsEnabled { get; private set; }
    internal string StatusText
    {
        get
        {
            return !_patchesInstalled ? "unavailable"
                : !IsEnabled ? "disabled"
                : "enabled | culling "
                    + Rate(_configuration.CullingRefreshRate.Value, 20f, 120f, 30f).ToString("F0")
                    + " Hz"
                    + " shadows "
                    + Rate(_configuration.DistantShadowRefreshRate.Value, 5f, 60f, 15f).ToString("F0")
                    + " Hz"
                    + " decals "
                    + Rate(_configuration.DeferredDecalRefreshRate.Value, 5f, 60f, 15f).ToString("F0")
                    + " Hz"
                    + " weather "
                    + Rate(_configuration.WeatherRefreshRate.Value, 5f, 60f, 10f).ToString("F0")
                    + " Hz"
                    + " | skipped "
                    + (_skippedCulling + _skippedDistantShadow + _skippedDecal + _skippedWeather);
        }
    }

    public void Initialize()
    {
        _instance = this;
        InstallPatches();
        SetEnabled(_configuration.WorldPresentationBudgetEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        ResetSchedule();
        _skippedCulling = _skippedDistantShadow = _skippedDecal = _skippedWeather = 0;
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        ResetSchedule();
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled && _patchesInstalled;
        _configuration.WorldPresentationBudgetEnabled.Value = enabled;
        ResetSchedule();
    }

    public void Shutdown()
    {
        _raidActive = false;
        IsEnabled = false;
        if (_harmony != null)
        {
            if (_cullingUpdate != null)
            {
                _harmony.Unpatch(_cullingUpdate, HarmonyPatchType.Prefix, _harmony.Id);
            }

            if (_distantShadowUpdate != null)
            {
                _harmony.Unpatch(_distantShadowUpdate, HarmonyPatchType.Prefix, _harmony.Id);
            }

            if (_decalUpdate != null)
            {
                _harmony.Unpatch(_decalUpdate, HarmonyPatchType.Prefix, _harmony.Id);
            }

            if (_weatherUpdate != null)
            {
                _harmony.Unpatch(_weatherUpdate, HarmonyPatchType.Prefix, _harmony.Id);
            }
        }
        _patchesInstalled = false;
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void InstallPatches()
    {
        try
        {
            Assembly assembly = typeof(Player).Assembly;
            _cullingUpdate = Find(assembly, "CullingManager", "Update");
            _distantShadowUpdate = Find(assembly, "DistantShadow", "Update");
            _decalUpdate = Find(assembly, "DeferredDecals.DeferredDecalRenderer", "Update");
            _weatherUpdate = Find(assembly, "EFT.Weather.WeatherController", "LateUpdate");
            if (_cullingUpdate == null || _distantShadowUpdate == null || _decalUpdate == null || _weatherUpdate == null)
            {
                throw new MissingMethodException("One or more profiled world-presentation callbacks changed signature.");
            }

            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.world-presentation-budget");
            _harmony.Patch(
                _cullingUpdate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(WorldPresentationRateFeature), nameof(CullingPrefix)))
            );
            _harmony.Patch(
                _distantShadowUpdate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(WorldPresentationRateFeature), nameof(DistantShadowPrefix)))
            );
            _harmony.Patch(
                _decalUpdate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(WorldPresentationRateFeature), nameof(DecalPrefix)))
            );
            _harmony.Patch(
                _weatherUpdate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(WorldPresentationRateFeature), nameof(WeatherPrefix)))
            );
            _patchesInstalled = true;
            _logger.LogWarning(
                Name
                    + " active: global culling and visual maintenance use independent rate caps; simulation and networking remain untouched."
            );
        }
        catch (Exception ex)
        {
            _patchesInstalled = false;
            _exceptions.Add(Name + " patch install", ex);
            _logger.LogWarning(Name + " unavailable; vanilla callbacks remain active: " + ex.Message);
        }
    }

    private static MethodInfo Find(Assembly assembly, string typeName, string methodName)
    {
        return assembly
            .GetType(typeName, false)
            ?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    }

    private static bool CullingPrefix()
    {
        WorldPresentationRateFeature feature = _instance;
        return feature == null
            || feature.ShouldRun(
                ref feature._nextCulling,
                feature._configuration.CullingRefreshRate.Value,
                20f,
                120f,
                30f,
                ref feature._skippedCulling
            );
    }

    private static bool DistantShadowPrefix()
    {
        WorldPresentationRateFeature feature = _instance;
        return feature == null
            || feature.ShouldRun(
                ref feature._nextDistantShadow,
                feature._configuration.DistantShadowRefreshRate.Value,
                5f,
                60f,
                15f,
                ref feature._skippedDistantShadow
            );
    }

    private static bool DecalPrefix()
    {
        WorldPresentationRateFeature feature = _instance;
        return feature == null
            || feature.ShouldRun(
                ref feature._nextDecal,
                feature._configuration.DeferredDecalRefreshRate.Value,
                5f,
                60f,
                15f,
                ref feature._skippedDecal
            );
    }

    private static bool WeatherPrefix()
    {
        WorldPresentationRateFeature feature = _instance;
        return feature == null
            || feature.ShouldRun(
                ref feature._nextWeather,
                feature._configuration.WeatherRefreshRate.Value,
                5f,
                60f,
                10f,
                ref feature._skippedWeather
            );
    }

    private bool ShouldRun(ref float next, float configuredRate, float minimum, float maximum, float fallback, ref long skipped)
    {
        if (!IsEnabled || !_raidActive)
        {
            return true;
        }

        float now = Time.realtimeSinceStartup;
        if (now < next)
        {
            skipped++;
            return false;
        }
        next = now + 1f / Rate(configuredRate, minimum, maximum, fallback);
        return true;
    }

    private void ResetSchedule()
    {
        _nextCulling = _nextDistantShadow = _nextDecal = _nextWeather = 0f;
    }

    private static float Rate(float value, float minimum, float maximum, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback
            : value < minimum ? minimum
            : value > maximum ? maximum
            : value;
    }
}
