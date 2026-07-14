using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using EFT;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal readonly struct DeclutterCounters
    {
        internal DeclutterCounters(int scanned, int candidates, int hidden, bool complete, double discoveryMs, double averageBatchMs)
        { Scanned = scanned; Candidates = candidates; Hidden = hidden; Complete = complete; DiscoveryMs = discoveryMs; AverageBatchMs = averageBatchMs; }
        internal int Scanned { get; }
        internal int Candidates { get; }
        internal int Hidden { get; }
        internal bool Complete { get; }
        internal double DiscoveryMs { get; }
        internal double AverageBatchMs { get; }
    }

    internal sealed class CosmeticDeclutterFeature : IPerformanceFeature
    {
        private static readonly string[] IncludeTokens = { "decal", "trash", "garbage", "rubbish", "litter", "debris", "puddle", "paper", "casing", "spent_shell", "shellcase", "shard" };
        private static readonly string[] ExcludeTokens = { "player", "bot", "weapon", "loot", "item", "container", "door", "quest", "extract", "switch", "lever", "button", "window", "wall", "floor", "building", "terrain" };
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private readonly Dictionary<Renderer, bool> _originalStates = new Dictionary<Renderer, bool>(1024);
        private Renderer[] _scan = Array.Empty<Renderer>();
        private int _cursor;
        private int _candidates;
        private bool _raidActive;
        private bool _scanRequested;
        private bool _lastDryRun;
        private double _discoveryMs;
        private double _averageBatchMs;
        private DeclutterCounters _counters;

        internal CosmeticDeclutterFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        { _logger = logger; _configuration = configuration; _exceptions = exceptions; }

        public string Name => "Cosmetic Declutter";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal bool DryRun => _configuration.CosmeticDeclutterDryRun.Value;
        internal DeclutterCounters Counters => _counters;
        internal string StatusText => IsEnabled ? (DryRun ? "enabled (dry-run)" : "enabled") : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize() { _breaker.Reset(); _lastDryRun = DryRun; SetEnabled(_configuration.CosmeticDeclutterEnabled.Value); }
        public void OnRaidStarted() { _raidActive = true; RequestScan(); }
        public void OnRaidEnded() { _raidActive = false; RestoreAll(); ClearScan(); }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.CosmeticDeclutterEnabled.Value = enabled;
            if (enabled && _raidActive) RequestScan();
            if (!enabled) { RestoreAll(); ClearScan(); }
            _logger.LogInfo(Name + " " + (enabled ? "enabled" : "disabled") + "; dry-run=" + DryRun + ".");
        }

        public void Shutdown() { _raidActive = false; IsEnabled = false; RestoreAll(); ClearScan(); }

        internal void Tick()
        {
            if (!IsEnabled || !_raidActive) return;
            if (DryRun != _lastDryRun)
            {
                RestoreAll();
                _lastDryRun = DryRun;
                RequestScan();
            }
            try
            {
                if (_scanRequested) Discover();
                if (_cursor < _scan.Length) ProcessBatch();
                _breaker.Success();
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogError(Name + " failed open: " + ex);
                if (_breaker.Failure()) { IsEnabled = false; _configuration.CosmeticDeclutterEnabled.Value = false; RestoreAll(); ClearScan(); }
            }
        }

        private void RequestScan()
        {
            if (!IsEnabled || !_raidActive) return;
            _scanRequested = true; _cursor = 0; _candidates = 0; _counters = default;
        }

        private void Discover()
        {
            long started = Stopwatch.GetTimestamp();
            _scan = Object.FindObjectsOfType<Renderer>(true);
            _discoveryMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _scanRequested = false;
            _logger.LogInfo(Name + " discovered " + _scan.Length + " renderers in " + _discoveryMs.ToString("F1") + " ms; classification is amortized across frames.");
        }

        private void ProcessBatch()
        {
            long started = Stopwatch.GetTimestamp();
            int batch = Clamp(_configuration.CosmeticDeclutterBatchSize.Value, 25, 1000);
            int end = Math.Min(_scan.Length, _cursor + batch);
            while (_cursor < end)
            {
                Renderer renderer = _scan[_cursor++];
                if (!IsCandidate(renderer)) continue;
                _candidates++;
                if (!DryRun && !_originalStates.ContainsKey(renderer))
                {
                    _originalStates.Add(renderer, renderer.forceRenderingOff);
                    renderer.forceRenderingOff = true;
                }
            }
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _averageBatchMs = _averageBatchMs == 0 ? elapsed : (_averageBatchMs * 0.9) + (elapsed * 0.1);
            bool complete = _cursor >= _scan.Length;
            _counters = new DeclutterCounters(_cursor, _candidates, DryRun ? 0 : _originalStates.Count, complete, _discoveryMs, _averageBatchMs);
            if (complete) _scan = Array.Empty<Renderer>();
        }

        private static bool IsCandidate(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff) return false;
            if (renderer.GetComponentInParent<Player>() != null) return false;
            string identity = HierarchyIdentity(renderer.transform);
            for (int i = 0; i < ExcludeTokens.Length; i++) if (identity.Contains(ExcludeTokens[i])) return false;
            bool included = false;
            for (int i = 0; i < IncludeTokens.Length; i++) if (identity.Contains(IncludeTokens[i])) { included = true; break; }
            if (!included) return false;
            if (renderer is MeshRenderer)
            {
                if (renderer.GetComponentInParent<Collider>() != null) return false;
                Vector3 size = renderer.bounds.size;
                if (size.sqrMagnitude > 16f) return false;
            }
            return renderer is MeshRenderer || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer;
        }

        private static string HierarchyIdentity(Transform transform)
        {
            string value = string.Empty;
            for (int i = 0; i < 4 && transform != null; i++, transform = transform.parent) value += "|" + transform.name.ToLowerInvariant();
            return value;
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, bool> item in _originalStates) if (item.Key != null) item.Key.forceRenderingOff = item.Value;
            _originalStates.Clear();
        }

        private void ClearScan() { _scan = Array.Empty<Renderer>(); _cursor = 0; _candidates = 0; _scanRequested = false; _counters = default; }
        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
