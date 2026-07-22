using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Reports remote skinned renderers currently reduced and restored.</summary>
internal readonly struct SkinningFeatureCounters
{
    internal SkinningFeatureCounters(int ai, int offscreenAi, int candidates, int modified, double averageMs)
    {
        RegisteredAi = ai;
        OffscreenAi = offscreenAi;
        CandidateRenderers = candidates;
        ModifiedRenderers = modified;
        AverageMs = averageMs;
    }

    internal int RegisteredAi { get; }
    internal int OffscreenAi { get; }
    internal int CandidateRenderers { get; }
    internal int ModifiedRenderers { get; }
    internal double AverageMs { get; }
}

/// <summary>Disables off-screen skin updates for safe remote entities and restores every renderer state.</summary>
internal sealed class RemoteAiOffscreenSkinningFeature : IPerformanceFeature
{
    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly EntityRegistry _registry;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private readonly Dictionary<SkinnedMeshRenderer, bool> _originalStates = new Dictionary<SkinnedMeshRenderer, bool>(256);
    private readonly Dictionary<int, float> _offscreenSince = new Dictionary<int, float>(64);
    private readonly HashSet<SkinnedMeshRenderer> _seenThisTick = new HashSet<SkinnedMeshRenderer>();
    private readonly HashSet<int> _seenEntities = new HashSet<int>();
    private readonly List<SkinnedMeshRenderer> _restoreBuffer = new List<SkinnedMeshRenderer>(64);
    private readonly List<int> _entityRemoveBuffer = new List<int>(16);
    private bool _raidActive;
    private float _nextUpdate;
    private double _averageMs;
    private SkinningFeatureCounters _counters;

    internal RemoteAiOffscreenSkinningFeature(
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
        get { return "Remote AI Offscreen Skinning"; }
    }

    public bool IsAvailable
    {
        get { return !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal SkinningFeatureCounters Counters
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
        SetEnabled(_configuration.SkinningEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        _nextUpdate = 0;
        _counters = default;
        _offscreenSince.Clear();
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        RestoreAll();
        _counters = default;
        _logger.LogInfo("Remote AI Offscreen Skinning restored every changed renderer at raid end.");
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
        _configuration.SkinningEnabled.Value = enabled;
        if (!enabled)
        {
            RestoreAll();
        }

        _logger.LogInfo($"Remote AI Offscreen Skinning {(enabled ? "enabled" : "disabled")}. Only confirmed remote AI can be considered.");
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

        ValidatedConfiguration configuration = _configuration.Validated;
        _nextUpdate = now + (float)configuration.SkinningUpdateIntervalSeconds;
        long started = Stopwatch.GetTimestamp();
        try
        {
            ProcessEntities(now, configuration);
            _breaker.Success();
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name, ex);
            _logger.LogError(Name + " failed open: " + ex);
            if (_breaker.Failure())
            {
                IsEnabled = false;
                _configuration.SkinningEnabled.Value = false;
                RestoreAll();
                _logger.LogError(
                    Name + " circuit breaker activated after repeated exceptions; all original renderer states were restored."
                );
            }
        }
        finally
        {
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _averageMs = _averageMs == 0 ? elapsed : (_averageMs * 0.95) + (elapsed * 0.05);
        }
    }

    private void ProcessEntities(float now, ValidatedConfiguration configuration)
    {
        if (!_registry.TryGetLocalPosition(out _))
        {
            return;
        }

        float distance = (float)configuration.SkinningDistance;
        float distanceSquared = distance * distance;
        int ai = 0;
        int offscreenAi = 0;
        int candidates = 0;
        _seenThisTick.Clear();
        _seenEntities.Clear();

        foreach (TrackedEntity entity in _registry.Entities)
        {
            if (entity.Kind != EntityKind.RemoteAI || entity.Player == null)
            {
                continue;
            }

            ai++;
            int id = entity.Player.GetInstanceID();
            _seenEntities.Add(id);
            bool alive = entity.IsAlive;
            bool beyond = entity.DistanceSquared > distanceSquared;
            bool visible = entity.IsVisible;
            if (!alive || !beyond || visible)
            {
                _offscreenSince.Remove(id);
                RestoreEntity(entity.SkinnedRenderers);
                continue;
            }

            offscreenAi++;
            if (!_offscreenSince.TryGetValue(id, out float since))
            {
                _offscreenSince.Add(id, now);
                continue;
            }
            if (now - since < configuration.SkinningOffscreenHoldSeconds)
            {
                continue;
            }

            SkinnedMeshRenderer[] renderers = entity.SkinnedRenderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                _seenThisTick.Add(renderer);
                if (_originalStates.ContainsKey(renderer))
                {
                    candidates++;
                    continue;
                }
                if (!renderer.updateWhenOffscreen)
                {
                    continue;
                }

                candidates++;
                _originalStates.Add(renderer, true);
                renderer.updateWhenOffscreen = false;
            }
        }

        RestoreNoLongerTracked();
        RemoveUnseenEntities();
        _counters = new SkinningFeatureCounters(ai, offscreenAi, candidates, _originalStates.Count, _averageMs);
    }

    private static bool IsVisible(Renderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible)
            {
                return true;
            }
        }
        return false;
    }

    private void RestoreEntity(SkinnedMeshRenderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            RestoreOne(renderers[i]);
        }
    }

    private void RestoreNoLongerTracked()
    {
        _restoreBuffer.Clear();
        foreach (KeyValuePair<SkinnedMeshRenderer, bool> pair in _originalStates)
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

    private void RemoveUnseenEntities()
    {
        _entityRemoveBuffer.Clear();
        foreach (KeyValuePair<int, float> pair in _offscreenSince)
        {
            if (!_seenEntities.Contains(pair.Key))
            {
                _entityRemoveBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _entityRemoveBuffer.Count; i++)
        {
            _offscreenSince.Remove(_entityRemoveBuffer[i]);
        }
    }

    private void RestoreOne(SkinnedMeshRenderer renderer)
    {
        if (ReferenceEquals(renderer, null))
        {
            return;
        }

        if (!_originalStates.TryGetValue(renderer, out bool original))
        {
            return;
        }

        if (renderer != null)
        {
            renderer.updateWhenOffscreen = original;
        }

        _originalStates.Remove(renderer);
    }

    private void RestoreAll()
    {
        foreach (KeyValuePair<SkinnedMeshRenderer, bool> pair in _originalStates)
        {
            if (pair.Key != null)
            {
                pair.Key.updateWhenOffscreen = pair.Value;
            }
        }

        _originalStates.Clear();
        _offscreenSince.Clear();
        _seenThisTick.Clear();
        _seenEntities.Clear();
        _restoreBuffer.Clear();
        _entityRemoveBuffer.Clear();
    }
}
