using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal readonly struct RemoteRenderLodCounters
    {
        internal RemoteRenderLodCounters(int ai, int mid, int far, int lodGroups, int skinned, int renderers, double averageMs)
        {
            RegisteredAi = ai; MidTierAi = mid; FarTierAi = far; ForcedLodGroups = lodGroups;
            ModifiedSkinnedRenderers = skinned; ModifiedRenderers = renderers; AverageMs = averageMs;
        }
        internal int RegisteredAi { get; }
        internal int MidTierAi { get; }
        internal int FarTierAi { get; }
        internal int ForcedLodGroups { get; }
        internal int ModifiedSkinnedRenderers { get; }
        internal int ModifiedRenderers { get; }
        internal double AverageMs { get; }
    }

    internal sealed class RemoteAiRenderLodFeature : IPerformanceFeature
    {
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly EntityRegistry _registry;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private readonly HashSet<LODGroup> _forcedLods = new HashSet<LODGroup>();
        private readonly Dictionary<SkinnedMeshRenderer, SkinnedState> _skinnedStates = new Dictionary<SkinnedMeshRenderer, SkinnedState>(256);
        private readonly Dictionary<Renderer, RendererState> _rendererStates = new Dictionary<Renderer, RendererState>(512);
        private readonly HashSet<LODGroup> _seenLods = new HashSet<LODGroup>();
        private readonly HashSet<SkinnedMeshRenderer> _seenSkinned = new HashSet<SkinnedMeshRenderer>();
        private readonly HashSet<Renderer> _seenRenderers = new HashSet<Renderer>();
        private readonly List<LODGroup> _lodRestore = new List<LODGroup>(32);
        private readonly List<SkinnedMeshRenderer> _skinnedRestore = new List<SkinnedMeshRenderer>(64);
        private readonly List<Renderer> _rendererRestore = new List<Renderer>(128);
        private bool _raidActive;
        private bool _lastDryRun;
        private float _nextUpdate;
        private double _averageMs;
        private RemoteRenderLodCounters _counters;

        internal RemoteAiRenderLodFeature(ManualLogSource logger, PluginConfiguration configuration, EntityRegistry registry, RecentExceptionLog exceptions)
        {
            _logger = logger; _configuration = configuration; _registry = registry; _exceptions = exceptions;
        }

        public string Name => "Remote AI Render LOD";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal bool DryRun => _configuration.RemoteAiRenderLodDryRun.Value;
        internal RemoteRenderLodCounters Counters => _counters;
        internal string StatusText => IsEnabled ? (DryRun ? "enabled (dry-run)" : "enabled") : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize() { _breaker.Reset(); _lastDryRun = DryRun; SetEnabled(_configuration.RemoteAiRenderLodEnabled.Value); }
        public void OnRaidStarted() { _raidActive = true; _nextUpdate = 0; _counters = default; }
        public void OnRaidEnded() { _raidActive = false; RestoreAll(); _counters = default; }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.RemoteAiRenderLodEnabled.Value = enabled;
            if (!enabled) RestoreAll();
            _logger.LogInfo(Name + " " + (enabled ? "enabled" : "disabled") + "; dry-run=" + DryRun + ".");
        }

        public void Shutdown() { _raidActive = false; IsEnabled = false; RestoreAll(); }

        internal void Tick(float now)
        {
            if (!IsEnabled || !_raidActive || now < _nextUpdate) return;
            float interval = Clamp(_configuration.RemoteAiRenderLodUpdateInterval.Value, 0.05f, 2f);
            _nextUpdate = now + interval;
            if (DryRun != _lastDryRun)
            {
                if (DryRun) RestoreAll();
                _lastDryRun = DryRun;
            }

            long started = Stopwatch.GetTimestamp();
            try { ProcessEntities(); _breaker.Success(); }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogError(Name + " failed open: " + ex);
                if (_breaker.Failure()) { IsEnabled = false; _configuration.RemoteAiRenderLodEnabled.Value = false; RestoreAll(); }
            }
            finally
            {
                double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                _averageMs = _averageMs == 0 ? elapsed : (_averageMs * 0.95) + (elapsed * 0.05);
            }
        }

        private void ProcessEntities()
        {
            if (!_registry.TryGetLocalPosition(out _)) return;
            float near = Clamp(_configuration.RemoteAiRenderLodNearDistance.Value, 10f, 200f);
            float far = Clamp(_configuration.RemoteAiRenderLodFarDistance.Value, near, 500f);
            float nearSquared = near * near;
            float farSquared = far * far;
            int ai = 0, mid = 0, farCount = 0, candidateLods = 0, candidateSkinned = 0, candidateRenderers = 0;
            _seenLods.Clear(); _seenSkinned.Clear(); _seenRenderers.Clear();

            foreach (TrackedEntity entity in _registry.Entities)
            {
                if (entity.Kind != EntityKind.RemoteAI || entity.Player == null) continue;
                ai++;
                bool alive = entity.IsAlive;
                float distanceSquared = entity.DistanceSquared;
                if (!alive || distanceSquared < nearSquared)
                {
                    RestoreEntity(entity);
                    continue;
                }

                bool farTier = distanceSquared >= farSquared;
                if (farTier) farCount++; else mid++;

                for (int i = 0; i < entity.LodGroups.Length; i++)
                {
                    LODGroup group = entity.LodGroups[i];
                    if (group == null || !group.enabled || group.lodCount == 0) continue;
                    candidateLods++;
                    _seenLods.Add(group);
                    if (DryRun) continue;
                    int forced = farTier ? group.lodCount - 1 : Math.Min(1, group.lodCount - 1);
                    group.ForceLOD(forced);
                    _forcedLods.Add(group);
                }

                for (int i = 0; i < entity.SkinnedRenderers.Length; i++)
                {
                    SkinnedMeshRenderer renderer = entity.SkinnedRenderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    candidateSkinned++;
                    _seenSkinned.Add(renderer);
                    if (DryRun) continue;
                    if (!_skinnedStates.ContainsKey(renderer)) _skinnedStates.Add(renderer, new SkinnedState(renderer.quality, renderer.skinnedMotionVectors));
                    renderer.quality = SkinQuality.Bone2;
                    renderer.skinnedMotionVectors = false;
                }

                for (int i = 0; i < entity.Renderers.Length; i++)
                {
                    Renderer renderer = entity.Renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    candidateRenderers++;
                    _seenRenderers.Add(renderer);
                    if (DryRun) continue;
                    if (!_rendererStates.ContainsKey(renderer))
                        _rendererStates.Add(renderer, new RendererState(renderer.reflectionProbeUsage, renderer.motionVectorGenerationMode, renderer.allowOcclusionWhenDynamic));
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderer.allowOcclusionWhenDynamic = true;
                }
            }

            if (!DryRun) RestoreNoLongerTracked();
            _counters = new RemoteRenderLodCounters(ai, mid, farCount, DryRun ? candidateLods : _forcedLods.Count,
                DryRun ? candidateSkinned : _skinnedStates.Count, DryRun ? candidateRenderers : _rendererStates.Count, _averageMs);
        }

        private void RestoreEntity(TrackedEntity entity)
        {
            for (int i = 0; i < entity.LodGroups.Length; i++) RestoreLod(entity.LodGroups[i]);
            for (int i = 0; i < entity.SkinnedRenderers.Length; i++) RestoreSkinned(entity.SkinnedRenderers[i]);
            for (int i = 0; i < entity.Renderers.Length; i++) RestoreRenderer(entity.Renderers[i]);
        }

        private void RestoreNoLongerTracked()
        {
            _lodRestore.Clear(); foreach (LODGroup item in _forcedLods) if (item == null || !_seenLods.Contains(item)) _lodRestore.Add(item);
            for (int i = 0; i < _lodRestore.Count; i++) RestoreLod(_lodRestore[i]);
            _skinnedRestore.Clear(); foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedState> item in _skinnedStates) if (item.Key == null || !_seenSkinned.Contains(item.Key)) _skinnedRestore.Add(item.Key);
            for (int i = 0; i < _skinnedRestore.Count; i++) RestoreSkinned(_skinnedRestore[i]);
            _rendererRestore.Clear(); foreach (KeyValuePair<Renderer, RendererState> item in _rendererStates) if (item.Key == null || !_seenRenderers.Contains(item.Key)) _rendererRestore.Add(item.Key);
            for (int i = 0; i < _rendererRestore.Count; i++) RestoreRenderer(_rendererRestore[i]);
        }

        private void RestoreLod(LODGroup group) { if (ReferenceEquals(group, null) || !_forcedLods.Remove(group)) return; if (group != null) group.ForceLOD(-1); }
        private void RestoreSkinned(SkinnedMeshRenderer renderer)
        {
            if (ReferenceEquals(renderer, null) || !_skinnedStates.TryGetValue(renderer, out SkinnedState state)) return;
            if (renderer != null) { renderer.quality = state.Quality; renderer.skinnedMotionVectors = state.MotionVectors; }
            _skinnedStates.Remove(renderer);
        }
        private void RestoreRenderer(Renderer renderer)
        {
            if (ReferenceEquals(renderer, null) || !_rendererStates.TryGetValue(renderer, out RendererState state)) return;
            if (renderer != null) { renderer.reflectionProbeUsage = state.ReflectionProbes; renderer.motionVectorGenerationMode = state.MotionVectors; renderer.allowOcclusionWhenDynamic = state.AllowOcclusion; }
            _rendererStates.Remove(renderer);
        }

        private void RestoreAll()
        {
            foreach (LODGroup group in _forcedLods) if (group != null) group.ForceLOD(-1);
            foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedState> item in _skinnedStates) if (item.Key != null) { item.Key.quality = item.Value.Quality; item.Key.skinnedMotionVectors = item.Value.MotionVectors; }
            foreach (KeyValuePair<Renderer, RendererState> item in _rendererStates) if (item.Key != null) { item.Key.reflectionProbeUsage = item.Value.ReflectionProbes; item.Key.motionVectorGenerationMode = item.Value.MotionVectors; item.Key.allowOcclusionWhenDynamic = item.Value.AllowOcclusion; }
            _forcedLods.Clear(); _skinnedStates.Clear(); _rendererStates.Clear(); _seenLods.Clear(); _seenSkinned.Clear(); _seenRenderers.Clear();
        }

        private static float Clamp(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;
        private readonly struct SkinnedState { internal SkinnedState(SkinQuality quality, bool motionVectors) { Quality = quality; MotionVectors = motionVectors; } internal SkinQuality Quality { get; } internal bool MotionVectors { get; } }
        private readonly struct RendererState
        {
            internal RendererState(ReflectionProbeUsage reflectionProbes, MotionVectorGenerationMode motionVectors, bool allowOcclusion) { ReflectionProbes = reflectionProbes; MotionVectors = motionVectors; AllowOcclusion = allowOcclusion; }
            internal ReflectionProbeUsage ReflectionProbes { get; }
            internal MotionVectorGenerationMode MotionVectors { get; }
            internal bool AllowOcclusion { get; }
        }
    }
}
