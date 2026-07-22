using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Reports how often ambient-light work was executed or safely reused.</summary>
internal readonly struct AreaLightCacheCounters
{
    internal AreaLightCacheCounters(long reused, long rebuilt, long shadowBypasses)
    {
        ReusedCommandBuffers = reused;
        RebuiltCommandBuffers = rebuilt;
        ShadowedLightBypasses = shadowBypasses;
    }

    internal long ReusedCommandBuffers { get; }
    internal long RebuiltCommandBuffers { get; }
    internal long ShadowedLightBypasses { get; }
}

/// <summary>
/// AreaLight normally clears and reconstructs its camera command buffer on every pre-cull.
/// The buffer already remains attached to that camera, so non-shadowed static lights can
/// safely reuse it for a few frames. Shadow-map and animated-cube lights remain vanilla.
/// </summary>
internal sealed class AreaLightCommandCacheFeature : IPerformanceFeature
{
    /// <summary>Tracks the next permitted ambient refresh independently for each camera.</summary>
    private sealed class CameraRefreshState
    {
        internal readonly Dictionary<int, RefreshEntry> ByCamera = new Dictionary<int, RefreshEntry>(2);
    }

    /// <summary>Stores one cached ambient command decision for a patched component.</summary>
    private readonly struct RefreshEntry
    {
        internal RefreshEntry(int nextFrame, int stateHash)
        {
            NextFrame = nextFrame;
            StateHash = stateHash;
        }

        internal int NextFrame { get; }
        internal int StateHash { get; }
    }

    private static AreaLightCommandCacheFeature _instance;
    private static ConditionalWeakTable<AreaLight, CameraRefreshState> _states = new ConditionalWeakTable<AreaLight, CameraRefreshState>();

    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private Harmony _harmony;
    private MethodInfo _setup;
    private MethodInfo _onEnable;
    private MethodInfo _onDisable;
    private bool _patchesInstalled;
    private long _reused;
    private long _rebuilt;
    private long _shadowBypasses;

    internal AreaLightCommandCacheFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "Static Area-Light Command Cache"; }
    }

    public bool IsAvailable
    {
        get { return _patchesInstalled && !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal AreaLightCacheCounters Counters
    {
        get
        {
            return new AreaLightCacheCounters(
                Interlocked.Read(ref _reused),
                Interlocked.Read(ref _rebuilt),
                Interlocked.Read(ref _shadowBypasses)
            );
        }
    }

    internal string StatusText
    {
        get
        {
            return IsEnabled
                    ? "enabled | reused "
                        + Interlocked.Read(ref _reused)
                        + " rebuilt "
                        + Interlocked.Read(ref _rebuilt)
                        + " shadow-safe bypass "
                        + Interlocked.Read(ref _shadowBypasses)
                : _breaker.IsOpen ? "disabled (circuit breaker)"
                : _patchesInstalled ? "disabled"
                : "unavailable";
        }
    }

    public void Initialize()
    {
        _instance = this;
        _breaker.Reset();
        try
        {
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.area-light-cache");
            _setup = AccessTools.Method(typeof(AreaLight), nameof(AreaLight.SetUpCommandBuffer), new[] { typeof(Camera) });
            _onEnable = AccessTools.Method(typeof(AreaLight), nameof(AreaLight.OnEnable), Type.EmptyTypes);
            _onDisable = AccessTools.Method(typeof(AreaLight), nameof(AreaLight.OnDisable), Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(typeof(AreaLightCommandCacheFeature), nameof(SetupPrefix));
            MethodInfo postfix = AccessTools.Method(typeof(AreaLightCommandCacheFeature), nameof(SetupPostfix));
            MethodInfo invalidate = AccessTools.Method(typeof(AreaLightCommandCacheFeature), nameof(InvalidatePostfix));
            if (_setup == null || _onEnable == null || _onDisable == null || prefix == null || postfix == null || invalidate == null)
            {
                throw new MissingMethodException("Expected AreaLight command-buffer methods were not found.");
            }

            _harmony.Patch(_setup, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            _harmony.Patch(_onEnable, postfix: new HarmonyMethod(invalidate));
            _harmony.Patch(_onDisable, postfix: new HarmonyMethod(invalidate));
            _patchesInstalled = true;
        }
        catch (Exception ex)
        {
            _patchesInstalled = false;
            _exceptions.Add(Name + " patch install", ex);
            _logger.LogWarning(Name + " unavailable; lighting remains vanilla. " + ex.Message);
        }
        SetEnabled(_configuration.AreaLightCacheEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _states = new ConditionalWeakTable<AreaLight, CameraRefreshState>();
        Interlocked.Exchange(ref _reused, 0);
        Interlocked.Exchange(ref _rebuilt, 0);
        Interlocked.Exchange(ref _shadowBypasses, 0);
    }

    public void OnRaidEnded()
    {
        _states = new ConditionalWeakTable<AreaLight, CameraRefreshState>();
    }

    public void SetEnabled(bool enabled)
    {
        bool desired = enabled && IsAvailable;
        if (IsEnabled == desired)
        {
            return;
        }

        IsEnabled = desired;
        _configuration.AreaLightCacheEnabled.Value = enabled;
        _states = new ConditionalWeakTable<AreaLight, CameraRefreshState>();
        _logger.LogInfo(
            Name + " " + (desired ? "enabled" : "disabled") + ". Shadowed and animated area lights always retain vanilla updates."
        );
    }

    public void Shutdown()
    {
        IsEnabled = false;
        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
        }

        _patchesInstalled = false;
        _states = new ConditionalWeakTable<AreaLight, CameraRefreshState>();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private bool ShouldBuild(AreaLight light, Camera camera)
    {
        if (!IsEnabled || light == null || camera == null)
        {
            return true;
        }
        // These paths generate or update real shadow textures and cannot be cached.
        if (light.m_Shadows || light.IsShadowCubeAnimated)
        {
            Interlocked.Increment(ref _shadowBypasses);
            return true;
        }

        int interval = Clamp(_configuration.AreaLightRefreshFrames.Value, 1, 8);
        if (interval <= 1)
        {
            return true;
        }

        CameraRefreshState state = _states.GetOrCreateValue(light);
        int cameraId = camera.GetInstanceID();
        int frame = Time.renderedFrameCount;
        int stateHash = StateHash(light);
        if (state.ByCamera.TryGetValue(cameraId, out RefreshEntry entry) && entry.StateHash == stateHash && frame < entry.NextFrame)
        {
            Interlocked.Increment(ref _reused);
            return false;
        }
        return true;
    }

    private void MarkBuilt(AreaLight light, Camera camera)
    {
        if (!IsEnabled || light == null || camera == null || light.m_Shadows || light.IsShadowCubeAnimated)
        {
            return;
        }

        int interval = Clamp(_configuration.AreaLightRefreshFrames.Value, 1, 8);
        CameraRefreshState state = _states.GetOrCreateValue(light);
        int cameraId = camera.GetInstanceID();
        bool firstBuild = !state.ByCamera.ContainsKey(cameraId);
        int delay = interval;
        if (firstBuild)
        {
            int phase = (light.GetInstanceID() ^ cameraId) & int.MaxValue;
            delay = 1 + phase % interval;
        }
        // Initial rebuilds are phase-staggered so thousands of Streets lights do not
        // converge on one periodic frame.
        state.ByCamera[cameraId] = new RefreshEntry(Time.renderedFrameCount + delay, StateHash(light));
        Interlocked.Increment(ref _rebuilt);
        _breaker.Success();
    }

    private void Failure(Exception ex)
    {
        _exceptions.Add(Name, ex);
        if (!_breaker.Failure())
        {
            return;
        }

        IsEnabled = false;
        _configuration.AreaLightCacheEnabled.Value = false;
        _logger.LogError(Name + " disabled after repeated failures; AreaLight returned to vanilla behavior. " + ex);
    }

    private static bool SetupPrefix(AreaLight __instance, Camera cam, out bool __state)
    {
        __state = false;
        AreaLightCommandCacheFeature instance = _instance;
        if (instance == null)
        {
            return true;
        }

        try
        {
            bool build = instance.ShouldBuild(__instance, cam);
            __state = build;
            return build;
        }
        catch (Exception ex)
        {
            instance.Failure(ex);
            return true;
        }
    }

    private static void SetupPostfix(AreaLight __instance, Camera cam, bool __state)
    {
        AreaLightCommandCacheFeature instance = _instance;
        if (!__state || instance == null)
        {
            return;
        }

        try
        {
            instance.MarkBuilt(__instance, cam);
        }
        catch (Exception ex)
        {
            instance.Failure(ex);
        }
    }

    private static void InvalidatePostfix(AreaLight __instance)
    {
        if (__instance != null)
        {
            _states.Remove(__instance);
        }
    }

    private static int StateHash(AreaLight light)
    {
        unchecked
        {
            int hash = light.m_Intensity.GetHashCode();
            hash = (hash * 397) ^ light.m_Color.GetHashCode();
            hash = (hash * 397) ^ light.m_SpecularScale.GetHashCode();
            hash = (hash * 397) ^ light.m_Hardness.GetHashCode();
            hash = (hash * 397) ^ light.m_Angle.GetHashCode();
            hash = (hash * 397) ^ light.size.GetHashCode();
            hash = (hash * 397) ^ light.length.GetHashCode();
            hash = (hash * 397) ^ light.depth.GetHashCode();
            hash = (hash * 397) ^ (light.m_Ambient ? 1 : 0);
            hash = (hash * 397) ^ (light.m_Negative ? 1 : 0);
            hash = (hash * 397) ^ (light.m_Specular ? 1 : 0);
            hash = (hash * 397) ^ (light.m_Spot ? 1 : 0);
            return hash;
        }
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum
            : value > maximum ? maximum
            : value;
    }
}
