using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class AggressiveQualityFeature : IPerformanceFeature
    {
        private static AggressiveQualityFeature _instance;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private QualitySnapshot _original;
        private bool _haveOriginal;
        private readonly Dictionary<AmbientLight, float> _ambientReflectionDelays = new Dictionary<AmbientLight, float>();
        private readonly Dictionary<AmbientLight, float> _ambientCommandNextRefresh = new Dictionary<AmbientLight, float>();
        private Harmony _harmony;
        private MethodInfo _ambientCommandRebuild;
        private long _ambientCommandRuns;
        private long _ambientCommandSkips;

        internal AggressiveQualityFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "Aggressive Global Render Profile";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal string StatusText => IsEnabled
            ? $"enabled | mip {QualitySettings.globalTextureMipmapLimit} | shadows {QualitySettings.shadowDistance:F0}m/{QualitySettings.shadowResolution}/{QualitySettings.shadowCascades} cascades | ambient reflections {ValidatedAmbientRate():F0} Hz | commands {ValidatedAmbientCommandRate():F0} Hz ({_ambientCommandSkips} skipped) | LOD untouched"
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            _instance = this;
            InstallAmbientCommandPatch();
            SetEnabled(_configuration.AggressiveModeEnabled.Value);
        }

        public void OnRaidStarted()
        {
            _ambientCommandRuns = 0;
            _ambientCommandSkips = 0;
            _ambientCommandNextRefresh.Clear();
            if (IsEnabled) Apply();
        }

        internal void Refresh()
        {
            if (IsEnabled) Apply();
        }

        public void OnRaidEnded()
        {
            // Keep the selected profile across raids so texture quality is established before the next map loads.
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.AggressiveModeEnabled.Value = enabled;
            if (enabled) Apply();
            else Restore();
        }

        public void Shutdown()
        {
            IsEnabled = false;
            Restore();
            if (_harmony != null && _ambientCommandRebuild != null)
                _harmony.Unpatch(_ambientCommandRebuild, HarmonyPatchType.Prefix, _harmony.Id);
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void InstallAmbientCommandPatch()
        {
            try
            {
                _ambientCommandRebuild = AccessTools.Method(typeof(AmbientLight), "method_1", Type.EmptyTypes);
                MethodInfo prefix = AccessTools.Method(typeof(AggressiveQualityFeature), nameof(AmbientCommandRebuildPrefix));
                if (_ambientCommandRebuild == null || prefix == null) throw new MissingMethodException("AmbientLight command-buffer rebuild method was not found.");
                _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.ambient-command-budget");
                _harmony.Patch(_ambientCommandRebuild, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                _ambientCommandRebuild = null;
                _exceptions.Add(Name + " ambient command patch", ex);
                _logger.LogWarning("Ambient command-buffer budget unavailable; vanilla ambient lighting remains active. " + ex.Message);
            }
        }

        private static bool AmbientCommandRebuildPrefix(AmbientLight __instance)
            => _instance == null || _instance.ShouldRebuildAmbientCommands(__instance);

        private bool ShouldRebuildAmbientCommands(AmbientLight light)
        {
            if (!IsEnabled || light == null || !light.IsInitialized || _ambientCommandRebuild == null) return true;
            float now = Time.realtimeSinceStartup;
            if (!_ambientCommandNextRefresh.TryGetValue(light, out float next) || now >= next)
            {
                _ambientCommandNextRefresh[light] = now + (1f / ValidatedAmbientCommandRate());
                _ambientCommandRuns++;
                return true;
            }
            _ambientCommandSkips++;
            return false;
        }

        private void Apply()
        {
            try
            {
                if (!_haveOriginal)
                {
                    _original = QualitySnapshot.Capture();
                    _haveOriginal = true;
                }

                QualitySettings.globalTextureMipmapLimit = Clamp(_configuration.AggressiveTextureMipLimit.Value, 0, 2);
                QualitySettings.shadowDistance = Clamp(_configuration.AggressiveShadowDistance.Value, 0f, 150f);
                QualitySettings.shadowResolution = _configuration.AggressiveShadowResolution.Value;
                QualitySettings.shadowCascades = ValidShadowCascades(_configuration.AggressiveShadowCascades.Value);
                QualitySettings.pixelLightCount = Clamp(_configuration.AggressivePixelLights.Value, 0, 4);
                QualitySettings.particleRaycastBudget = Clamp(_configuration.AggressiveParticleRaycastBudget.Value, 0, 256);
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = false;
                QualitySettings.skinWeights = SkinWeights.TwoBones;
                ApplyAmbientReflectionBudget();
                _breaker.Success();
                _logger.LogWarning(Name + " applied. This deliberately reduces visual quality; change texture mip limits before loading a raid whenever possible.");
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogError(Name + " failed open: " + ex);
                if (_breaker.Failure())
                {
                    IsEnabled = false;
                    _configuration.AggressiveModeEnabled.Value = false;
                    Restore();
                }
            }
        }

        private void Restore()
        {
            if (!_haveOriginal) return;
            try { _original.Apply(); }
            catch (Exception ex) { _exceptions.Add(Name + " restore", ex); }
            foreach (KeyValuePair<AmbientLight, float> pair in _ambientReflectionDelays)
                if (pair.Key != null) pair.Key.RenderDelay = pair.Value;
            _ambientReflectionDelays.Clear();
            _ambientCommandNextRefresh.Clear();
            _haveOriginal = false;
            _logger.LogInfo(Name + " restored the previous Unity quality settings.");
        }

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static int ValidShadowCascades(int value) => value >= 4 ? 4 : value >= 2 ? 2 : 0;
        private static float Clamp(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;

        private float ValidatedAmbientRate() => Clamp(_configuration.AggressiveAmbientReflectionRate.Value, 5f, 30f);
        private float ValidatedAmbientCommandRate() => Clamp(_configuration.AggressiveAmbientCommandRate.Value, 8f, 60f);

        private void ApplyAmbientReflectionBudget()
        {
            float minimumDelay = 1f / ValidatedAmbientRate();
            AmbientLight[] lights = UnityEngine.Object.FindObjectsOfType<AmbientLight>();
            for (int i = 0; i < lights.Length; i++)
            {
                AmbientLight light = lights[i];
                if (light == null) continue;
                if (!_ambientReflectionDelays.ContainsKey(light)) _ambientReflectionDelays.Add(light, light.RenderDelay);
                if (light.RenderDelay < minimumDelay) light.RenderDelay = minimumDelay;
            }
        }

        private readonly struct QualitySnapshot
        {
            internal QualitySnapshot(int mipLimit, float shadowDistance, ShadowResolution shadowResolution, int shadowCascades, int pixelLights, int particleBudget,
                bool realtimeReflections, bool softParticles, SkinWeights skinWeights)
            {
                MipLimit = mipLimit;
                ShadowDistance = shadowDistance;
                ShadowResolution = shadowResolution;
                ShadowCascades = shadowCascades;
                PixelLights = pixelLights;
                ParticleBudget = particleBudget;
                RealtimeReflections = realtimeReflections;
                SoftParticles = softParticles;
                SkinWeights = skinWeights;
            }

            private int MipLimit { get; }
            private float ShadowDistance { get; }
            private ShadowResolution ShadowResolution { get; }
            private int ShadowCascades { get; }
            private int PixelLights { get; }
            private int ParticleBudget { get; }
            private bool RealtimeReflections { get; }
            private bool SoftParticles { get; }
            private SkinWeights SkinWeights { get; }

            internal static QualitySnapshot Capture() => new QualitySnapshot(
                QualitySettings.globalTextureMipmapLimit,
                QualitySettings.shadowDistance,
                QualitySettings.shadowResolution,
                QualitySettings.shadowCascades,
                QualitySettings.pixelLightCount,
                QualitySettings.particleRaycastBudget,
                QualitySettings.realtimeReflectionProbes,
                QualitySettings.softParticles,
                QualitySettings.skinWeights);

            internal void Apply()
            {
                QualitySettings.globalTextureMipmapLimit = MipLimit;
                QualitySettings.shadowDistance = ShadowDistance;
                QualitySettings.shadowResolution = ShadowResolution;
                QualitySettings.shadowCascades = ShadowCascades;
                QualitySettings.pixelLightCount = PixelLights;
                QualitySettings.particleRaycastBudget = ParticleBudget;
                QualitySettings.realtimeReflectionProbes = RealtimeReflections;
                QualitySettings.softParticles = SoftParticles;
                QualitySettings.skinWeights = SkinWeights;
            }
        }
    }
}
