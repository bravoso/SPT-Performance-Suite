using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Uses reflection to rate-limit optional Dynamic Maps work without creating a hard dependency.</summary>
internal sealed class DynamicMapsCompatibilityFeature : IPerformanceFeature
{
    private const string PluginGuid = "com.mpstark.dynamicmaps";
    private static DynamicMapsCompatibilityFeature _instance;

    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private Harmony _harmony;
    private MethodInfo _precacheCoroutine;
    private MethodInfo _onCenter;
    private BaseUnityPlugin _dynamicMapsPlugin;
    private Func<object, bool> _isShowingMiniMap;
    private bool _patchInstalled;
    private float _nextDiscovery;
    private float _nextMiniMapCenter;

    internal DynamicMapsCompatibilityFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "Dynamic Maps CPU Budget"; }
    }

    public bool IsAvailable
    {
        get { return _patchInstalled && !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal string StatusText
    {
        get
        {
            if (!_patchInstalled)
            {
                return "not installed or incompatible";
            }

            if (!IsEnabled)
            {
                return _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";
            }

            return "enabled | minimap recenter "
                + Clamp(_configuration.DynamicMapsMiniMapRefreshRate.Value, 15f, 60f).ToString("F0")
                + " Hz | input/full map uncapped | all-map image preload blocked"
                + (_configuration.DynamicMapsLeanMarkers.Value ? " | lean markers" : string.Empty);
        }
    }

    public void Initialize()
    {
        _instance = this;
        _breaker.Reset();
        DiscoverAndPatch();
        SetEnabled(_configuration.DynamicMapsOptimizationEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _nextMiniMapCenter = 0;
        if (IsEnabled)
        {
            ApplyLeanMarkerConfiguration();
        }
    }

    public void OnRaidEnded()
    {
        _nextMiniMapCenter = 0;
    }

    internal void Tick(float now)
    {
        if (_patchInstalled || now < _nextDiscovery)
        {
            return;
        }

        _nextDiscovery = now + 1f;
        DiscoverAndPatch();
        if (_patchInstalled && _configuration.DynamicMapsOptimizationEnabled.Value && _configuration.OptimizationsEnabled.Value)
        {
            IsEnabled = true;
            ApplyLeanMarkerConfiguration();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && (!IsAvailable || _breaker.IsOpen))
        {
            return;
        }

        IsEnabled = enabled;
        _configuration.DynamicMapsOptimizationEnabled.Value = enabled;
        _nextMiniMapCenter = 0;
        if (enabled)
        {
            ApplyLeanMarkerConfiguration();
        }
    }

    public void Shutdown()
    {
        IsEnabled = false;
        if (_harmony != null && _precacheCoroutine != null)
        {
            _harmony.Unpatch(_precacheCoroutine, HarmonyPatchType.Prefix, _harmony.Id);
        }

        if (_harmony != null && _onCenter != null)
        {
            _harmony.Unpatch(_onCenter, HarmonyPatchType.Prefix, _harmony.Id);
        }

        _patchInstalled = false;
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void DiscoverAndPatch()
    {
        if (_patchInstalled)
        {
            return;
        }

        try
        {
            BepInEx.PluginInfo info = FindPlugin(PluginGuid);
            _dynamicMapsPlugin = info?.Instance as BaseUnityPlugin;
            Assembly assembly = _dynamicMapsPlugin?.GetType().Assembly;
            Type mapScreen = assembly?.GetType("DynamicMaps.UI.ModdedMapScreen", false);
            _precacheCoroutine = mapScreen?.GetMethod("PrecacheCoroutine", BindingFlags.Static | BindingFlags.NonPublic);
            _onCenter = mapScreen?.GetMethod("OnCenter", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo showingMiniMapGetter = mapScreen
                ?.GetProperty("_showingMiniMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetGetMethod(true);
            MethodInfo precachePrefix = AccessTools.Method(typeof(DynamicMapsCompatibilityFeature), nameof(PrecachePrefix));
            MethodInfo miniMapCenterPrefix = AccessTools.Method(typeof(DynamicMapsCompatibilityFeature), nameof(MiniMapCenterPrefix));
            if (
                _dynamicMapsPlugin == null
                || _precacheCoroutine == null
                || _onCenter == null
                || showingMiniMapGetter == null
                || precachePrefix == null
                || miniMapCenterPrefix == null
            )
            {
                _patchInstalled = false;
                return;
            }
            _isShowingMiniMap = BuildBoolGetter(mapScreen, showingMiniMapGetter);
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.dynamicmaps");
            _harmony.Patch(_precacheCoroutine, prefix: new HarmonyMethod(precachePrefix));
            _harmony.Patch(_onCenter, prefix: new HarmonyMethod(miniMapCenterPrefix));
            _patchInstalled = true;
            _logger.LogWarning(
                "Dynamic Maps compatibility active: minimap recentering is rate-limited without throttling map input; all-map image preloading is blocked and marker providers are reduced to the requested set."
            );
        }
        catch (Exception ex)
        {
            _patchInstalled = false;
            _exceptions.Add(Name + " patch", ex);
            _logger.LogWarning(Name + " unavailable; Dynamic Maps remains unchanged: " + ex.Message);
        }
    }

    private static bool PrecachePrefix(ref IEnumerator __result)
    {
        DynamicMapsCompatibilityFeature instance = _instance;
        if (instance == null || !instance.IsEnabled)
        {
            return true;
        }

        __result = EmptyCoroutine();
        return false;
    }

    private static bool MiniMapCenterPrefix(object __instance)
    {
        DynamicMapsCompatibilityFeature instance = _instance;
        if (instance == null || !instance.IsEnabled || instance._isShowingMiniMap == null)
        {
            return true;
        }

        try
        {
            // OnCenter is nearly free on the full map unless its centering hotkey is used,
            // so never throttle it there. The minimap path performs a map shift and layer
            // selection every rendered frame and is the measured Dynamic Maps hot path.
            if (!instance._isShowingMiniMap(__instance))
            {
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (now < instance._nextMiniMapCenter)
            {
                return false;
            }

            float rate = Clamp(instance._configuration.DynamicMapsMiniMapRefreshRate.Value, 15f, 60f);
            instance._nextMiniMapCenter = now + 1f / rate;
            return true;
        }
        catch (Exception ex)
        {
            instance.RecordFailure("minimap recenter", ex);
            return true;
        }
    }

    private static IEnumerator EmptyCoroutine()
    {
        yield break;
    }

    private static Func<object, bool> BuildBoolGetter(Type declaringType, MethodInfo getter)
    {
        ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
        UnaryExpression converted = Expression.Convert(instance, declaringType);
        MethodCallExpression call = Expression.Call(converted, getter);
        return Expression.Lambda<Func<object, bool>>(call, instance).Compile();
    }

    private void RecordFailure(string operation, Exception ex)
    {
        _exceptions.Add(Name + " " + operation, ex);
        if (_breaker.Failure())
        {
            IsEnabled = false;
            _logger.LogWarning(
                Name + " disabled after repeated " + operation + " failures; Dynamic Maps now runs unchanged: " + ex.Message
            );
        }
    }

    private void ApplyLeanMarkerConfiguration()
    {
        if (!_configuration.DynamicMapsLeanMarkers.Value || _dynamicMapsPlugin == null)
        {
            return;
        }

        try
        {
            ConfigFile config = _dynamicMapsPlugin.Config;
            Set(config, "2. Dynamic Markers", "Show Player Marker", true);
            Set(config, "2. Dynamic Markers", "Show Friendly Player Markers", true);
            Set(config, "2. Dynamic Markers", "Show Enemy Player Markers", false);
            Set(config, "2. Dynamic Markers", "Show Scav Markers", false);
            Set(config, "2. Dynamic Markers", "Show Boss Markers", false);
            Set(config, "2. Dynamic Markers", "Show Quests In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Player-killed Corpses In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Friendly-killed Corpses In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Friendly Corpses In Raid", false);
            Set(config, "2. Dynamic Markers", "Show Boss Corpses In Raid", false);
            Set(config, "2. Dynamic Markers", "Show Other Corpses In Raid", false);
            // Navigation and recovery markers are intentionally retained. Their state changes
            // rarely and they are useful enough that removing them is a poor CPU tradeoff.
            Set(config, "2. Dynamic Markers", "Show Locked Door Status", true);
            Set(config, "2. Dynamic Markers", "Show Extracts In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Extracts Status In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Transit Points In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Secret Extracts In Raid", true);
            Set(config, "2. Dynamic Markers", "Show Dropped Backpack In Raid", true);
            Set(config, "2. Dynamic Markers", "Show wish listed items In Raid", false);
            Set(config, "2. Dynamic Markers", "Show BTR In Raid", false);
            Set(config, "2. Dynamic Markers", "Show Airdrops In Raid", false);
            Set(config, "2. Dynamic Markers", "Show Hidden Stashes In Raid", false);
            Set(config, "7. External Mod Support", "Show Heli Crash Marker", false);
            config.Save();
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name + " marker configuration", ex);
            _logger.LogWarning("Dynamic Maps marker filtering failed open; refresh limiting remains active: " + ex.Message);
        }
    }

    private static void Set(ConfigFile config, string section, string key, bool value)
    {
        if (config.TryGetEntry(new ConfigDefinition(section, key), out ConfigEntry<bool> entry) && entry.Value != value)
        {
            entry.Value = value;
        }
    }

    private static BepInEx.PluginInfo FindPlugin(string guid)
    {
        foreach (KeyValuePair<string, BepInEx.PluginInfo> pair in Chainloader.PluginInfos)
        {
            if (string.Equals(pair.Value?.Metadata?.GUID, guid, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? minimum
            : value < minimum ? minimum
            : value > maximum ? maximum
            : value;
    }
}
