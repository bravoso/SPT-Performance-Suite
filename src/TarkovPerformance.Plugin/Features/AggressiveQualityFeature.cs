using System;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class AggressiveQualityFeature : IPerformanceFeature
    {
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private QualitySnapshot _original;
        private bool _haveOriginal;

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
            ? $"enabled | mip {QualitySettings.globalTextureMipmapLimit} | shadows {QualitySettings.shadowDistance:F0}m | LOD untouched"
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            SetEnabled(_configuration.AggressiveModeEnabled.Value);
        }

        public void OnRaidStarted()
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
                QualitySettings.pixelLightCount = Clamp(_configuration.AggressivePixelLights.Value, 0, 4);
                QualitySettings.particleRaycastBudget = Clamp(_configuration.AggressiveParticleRaycastBudget.Value, 0, 256);
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.softParticles = false;
                QualitySettings.skinWeights = SkinWeights.TwoBones;
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
            _haveOriginal = false;
            _logger.LogInfo(Name + " restored the previous Unity quality settings.");
        }

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static float Clamp(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;

        private readonly struct QualitySnapshot
        {
            internal QualitySnapshot(int mipLimit, float shadowDistance, int pixelLights, int particleBudget,
                bool realtimeReflections, bool softParticles, SkinWeights skinWeights)
            {
                MipLimit = mipLimit;
                ShadowDistance = shadowDistance;
                PixelLights = pixelLights;
                ParticleBudget = particleBudget;
                RealtimeReflections = realtimeReflections;
                SoftParticles = softParticles;
                SkinWeights = skinWeights;
            }

            private int MipLimit { get; }
            private float ShadowDistance { get; }
            private int PixelLights { get; }
            private int ParticleBudget { get; }
            private bool RealtimeReflections { get; }
            private bool SoftParticles { get; }
            private SkinWeights SkinWeights { get; }

            internal static QualitySnapshot Capture() => new QualitySnapshot(
                QualitySettings.globalTextureMipmapLimit,
                QualitySettings.shadowDistance,
                QualitySettings.pixelLightCount,
                QualitySettings.particleRaycastBudget,
                QualitySettings.realtimeReflectionProbes,
                QualitySettings.softParticles,
                QualitySettings.skinWeights);

            internal void Apply()
            {
                QualitySettings.globalTextureMipmapLimit = MipLimit;
                QualitySettings.shadowDistance = ShadowDistance;
                QualitySettings.pixelLightCount = PixelLights;
                QualitySettings.particleRaycastBudget = ParticleBudget;
                QualitySettings.realtimeReflectionProbes = RealtimeReflections;
                QualitySettings.softParticles = SoftParticles;
                QualitySettings.skinWeights = SkinWeights;
            }
        }
    }
}
