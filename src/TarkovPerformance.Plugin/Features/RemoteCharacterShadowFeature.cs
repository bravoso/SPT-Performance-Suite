using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Reports remote shadow casters currently reduced and restored.</summary>
internal readonly struct ShadowFeatureCounters
{
    internal ShadowFeatureCounters(int ai, int beyond, int tracked, int disabled, double effectiveDistance, double averageMs)
    {
        RegisteredAi = ai;
        BeyondThreshold = beyond;
        TrackedRenderers = tracked;
        DisabledRenderers = disabled;
        EffectiveDistance = effectiveDistance;
        AverageMs = averageMs;
    }

    internal int RegisteredAi { get; }
    internal int BeyondThreshold { get; }
    internal int TrackedRenderers { get; }
    internal int DisabledRenderers { get; }
    internal double EffectiveDistance { get; }
    internal double AverageMs { get; }
}

/// <summary>Distance-budgets remote character shadows while preserving local and protected entity visuals.</summary>
internal sealed class RemoteCharacterShadowFeature : IPerformanceFeature
{
    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly EntityRegistry _registry;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private readonly AdaptiveDistanceController _adaptiveDistance = new AdaptiveDistanceController();
    private readonly Dictionary<Renderer, ShadowCastingMode> _originalStates = new Dictionary<Renderer, ShadowCastingMode>(512);
    private readonly HashSet<Renderer> _seenThisTick = new HashSet<Renderer>();
    private readonly List<Renderer> _restoreBuffer = new List<Renderer>(128);
    private bool _raidActive;
    private float _nextUpdate;
    private double _averageMs;
    private ShadowFeatureCounters _counters;

    internal RemoteCharacterShadowFeature(
        ManualLogSource logger,
        PluginConfiguration configuration,
        EntityRegistry registry,
        RecentExceptionLog exceptions
    )
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "Remote Character Shadow LOD"; }
    }

    public bool IsAvailable
    {
        get { return !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal ShadowFeatureCounters Counters
    {
        get { return _counters; }
    }

    internal string StatusText
    {
        get
        {
            return IsEnabled ? "enabled"
                : _breaker.IsOpen ? "disabled (circuit breaker)"
                : "disabled";
        }
    }

    public void Initialize()
    {
        _breaker.Reset();
        SetEnabled(_configuration.ShadowEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        _nextUpdate = 0;
        _adaptiveDistance.Reset(_configuration.Validated.ShadowDistance);
        _counters = default;
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        RestoreAll();
        _counters = default;
        _logger.LogInfo("Remote Character Shadow LOD restored all tracked renderer states at raid end.");
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && _breaker.IsOpen)
        {
            return;
        }

        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        _configuration.ShadowEnabled.Value = enabled;
        if (!enabled)
        {
            RestoreAll();
        }

        _logger.LogInfo($"Remote Character Shadow LOD {(enabled ? "enabled" : "disabled")}. Existing renderers restored when disabling.");
    }

    public void Shutdown()
    {
        _raidActive = false;
        IsEnabled = false;
        RestoreAll();
    }

    internal void Tick(float now)
    {
        if (!IsEnabled || !_raidActive || now < _nextUpdate)
        {
            return;
        }

        _nextUpdate = now + (float)_configuration.Validated.UpdateIntervalSeconds;
        long started = Stopwatch.GetTimestamp();
        try
        {
            ProcessEntities();
            _breaker.Success();
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name, ex);
            _logger.LogError(Name + " failed open: " + ex);
            if (_breaker.Failure())
            {
                IsEnabled = false;
                _configuration.ShadowEnabled.Value = false;
                RestoreAll();
                _logger.LogError(Name + " circuit breaker activated after repeated exceptions. Original shadow states were restored.");
            }
        }
        finally
        {
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _averageMs = _averageMs == 0 ? elapsed : (_averageMs * 0.95) + (elapsed * 0.05);
        }
    }

    internal void ObserveFrame(double deltaSeconds, double cpuMainThreadMs)
    {
        if (!IsEnabled || !_raidActive)
        {
            return;
        }

        ValidatedConfiguration configuration = _configuration.Validated;
        if (_configuration.ShadowAdaptiveEnabled.Value)
        {
            _adaptiveDistance.Update(
                deltaSeconds,
                cpuMainThreadMs,
                configuration.ShadowDistance,
                configuration.ShadowMinimumDistance,
                configuration.ShadowTargetFps
            );
        }
        else if (Math.Abs(_adaptiveDistance.EffectiveDistance - configuration.ShadowDistance) > 0.01)
        {
            _adaptiveDistance.Reset(configuration.ShadowDistance);
        }
    }

    private void ProcessEntities()
    {
        if (!_registry.TryGetLocalPosition(out _))
        {
            return;
        }

        ValidatedConfiguration configuration = _configuration.Validated;
        float distance = (float)(
            _configuration.ShadowAdaptiveEnabled.Value ? _adaptiveDistance.EffectiveDistance : configuration.ShadowDistance
        );
        if (distance <= 0)
        {
            distance = (float)configuration.ShadowDistance;
        }

        float distanceSquared = distance * distance;
        int ai = 0,
            beyond = 0,
            tracked = 0,
            disabled = 0;
        _seenThisTick.Clear();

        foreach (TrackedEntity entity in _registry.Entities)
        {
            if (entity.Kind != EntityKind.RemoteAI || entity.Player == null)
            {
                continue;
            }

            ai++;
            bool isBeyond = entity.DistanceSquared > distanceSquared;
            if (isBeyond)
            {
                beyond++;
            }

            Renderer[] renderers = entity.Renderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsEligibleRenderer(renderer))
                {
                    continue;
                }

                tracked++;
                _seenThisTick.Add(renderer);
                if (isBeyond)
                {
                    if (renderer.shadowCastingMode == ShadowCastingMode.Off)
                    {
                        continue;
                    }

                    if (!_originalStates.ContainsKey(renderer))
                    {
                        _originalStates.Add(renderer, renderer.shadowCastingMode);
                    }

                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    disabled++;
                }
                else
                {
                    RestoreOne(renderer);
                }
            }
        }

        RestoreNoLongerTracked();
        _counters = new ShadowFeatureCounters(ai, beyond, tracked, _originalStates.Count, distance, _averageMs);
    }

    private static bool IsEligibleRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        return renderer is SkinnedMeshRenderer || renderer is MeshRenderer;
    }

    private void RestoreNoLongerTracked()
    {
        _restoreBuffer.Clear();
        foreach (KeyValuePair<Renderer, ShadowCastingMode> pair in _originalStates)
        {
            if (pair.Key == null || !_seenThisTick.Contains(pair.Key))
            {
                _restoreBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _restoreBuffer.Count; i++)
        {
            RestoreOne(_restoreBuffer[i]);
        }
    }

    private void RestoreOne(Renderer renderer)
    {
        if (!_originalStates.TryGetValue(renderer, out ShadowCastingMode original))
        {
            return;
        }

        if (renderer != null)
        {
            renderer.shadowCastingMode = original;
        }

        _originalStates.Remove(renderer);
    }

    private void RestoreAll()
    {
        foreach (KeyValuePair<Renderer, ShadowCastingMode> pair in _originalStates)
        {
            if (pair.Key != null)
            {
                pair.Key.shadowCastingMode = pair.Value;
            }
        }

        _originalStates.Clear();
        _seenThisTick.Clear();
        _restoreBuffer.Clear();
    }
}
