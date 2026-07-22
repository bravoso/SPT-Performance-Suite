using UnityEngine;

namespace TarkovPerformanceSuite.Configuration;

/// <summary>
/// Applies the complete set of values owned by a named performance preset.
/// </summary>
/// <remarks>
/// Keeping preset values in one place prevents the BepInEx plugin lifecycle from becoming a second
/// configuration system. Callers are responsible for temporarily suppressing config-file writes so
/// the group is persisted atomically after every value has been assigned.
/// </remarks>
internal static class PerformancePresetApplier
{
    internal static void Apply(PluginConfiguration configuration, PerformancePreset preset)
    {
        ApplyCommonValues(configuration);

        switch (preset)
        {
            case PerformancePreset.Balanced:
                ApplyBalanced(configuration);
                break;
            case PerformancePreset.Performance:
                ApplyPerformance(configuration);
                break;
            case PerformancePreset.Extreme:
                ApplyExtreme(configuration);
                break;
            case PerformancePreset.Custom:
            default:
                break;
        }
    }

    private static void ApplyCommonValues(PluginConfiguration configuration)
    {
        configuration.KnownModFixesEnabled.Value = true;
        configuration.DynamicMapsOptimizationEnabled.Value = true;
        configuration.ExportJson.Value = false;
        configuration.FramePacingEnabled.Value = true;
        configuration.RemoteUpdateBudgetEnabled.Value = false;
        configuration.RemoteAnimatorCullingEnabled.Value = false;
        configuration.RemotePresentationBudgetEnabled.Value = false;
        configuration.RemoteComplexLateUpdateBudgetEnabled.Value = false;
        configuration.UseAllLogicalProcessors.Value = true;
        configuration.HotPathLogSuppressionEnabled.Value = true;
        configuration.CombatPresentationEnabled.Value = true;
        configuration.SoundOnlyRemoteShots.Value = false;
        configuration.CullDistantMuzzleEffects.Value = true;
        configuration.CullDistantImpactEffects.Value = true;
        configuration.CullHiddenRemoteLights.Value = true;
        configuration.RequireBakedOcclusionForSoundOnly.Value = true;
        configuration.AggressiveModeEnabled.Value = true;
        configuration.AreaLightCacheEnabled.Value = true;
        configuration.WorldPresentationBudgetEnabled.Value = true;
        configuration.SkinningEnabled.Value = true;
        configuration.ShadowEnabled.Value = true;
        configuration.CosmeticDeclutterEnabled.Value = true;
        configuration.PipScopeOptimizationEnabled.Value = true;
        configuration.HeadlessAuthorityEnabled.Value = false;
        configuration.RemoteFreezeHiddenPresentation.Value = false;
        configuration.OptimizationsEnabled.Value = true;
    }

    private static void ApplyBalanced(PluginConfiguration configuration)
    {
        configuration.AggressiveTextureMipLimit.Value = 0;
        configuration.AggressiveShadowDistance.Value = 75f;
        configuration.AggressiveShadowResolution.Value = ShadowResolution.Medium;
        configuration.AggressiveShadowCascades.Value = 2;
        configuration.AggressivePixelLights.Value = 2;
        configuration.AggressiveParticleRaycastBudget.Value = 64;
        configuration.AggressiveAmbientReflectionRate.Value = 20f;
        configuration.AggressiveAmbientCommandRate.Value = 30f;
        configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
        configuration.RemoteUpdateBudgetDistance.Value = 60f;
        configuration.RemoteUpdateBudgetHold.Value = 0.3f;
        configuration.RemoteUpdateBudgetDivisor.Value = 2;
        configuration.RemoteAggressivePresentationDistance.Value = 60f;
        configuration.RemoteVisiblePresentationDivisor.Value = 2;
        configuration.ShadowDistance.Value = 100f;
        configuration.ShadowMinimumDistance.Value = 60f;
        configuration.PipScopeResolutionScale.Value = 1f;
        configuration.AreaLightRefreshFrames.Value = 2;
        configuration.CullingRefreshRate.Value = 60f;
        configuration.DistantShadowRefreshRate.Value = 30f;
        configuration.DeferredDecalRefreshRate.Value = 30f;
        configuration.WeatherRefreshRate.Value = 20f;
        configuration.SoundOnlyShotDistance.Value = 180f;
        configuration.IncomingShotSafetyRadius.Value = 45f;
        configuration.RemoteCombatRecentVisibilityHold.Value = 1f;
        configuration.DistantMuzzleEffectDistance.Value = 75f;
        configuration.DistantImpactEffectDistance.Value = 120f;
        configuration.HiddenRemoteLightDistance.Value = 90f;
    }

    private static void ApplyPerformance(PluginConfiguration configuration)
    {
        configuration.AggressiveTextureMipLimit.Value = 0;
        configuration.AggressiveShadowDistance.Value = 45f;
        configuration.AggressiveShadowResolution.Value = ShadowResolution.Low;
        configuration.AggressiveShadowCascades.Value = 2;
        configuration.AggressivePixelLights.Value = 1;
        configuration.AggressiveParticleRaycastBudget.Value = 16;
        configuration.AggressiveAmbientReflectionRate.Value = 10f;
        configuration.AggressiveAmbientCommandRate.Value = 15f;
        configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
        configuration.RemoteUpdateBudgetDistance.Value = 25f;
        configuration.RemoteUpdateBudgetHold.Value = 0.1f;
        configuration.RemoteUpdateBudgetDivisor.Value = 8;
        configuration.RemoteAggressivePresentationDistance.Value = 50f;
        configuration.RemoteVisiblePresentationDivisor.Value = 2;
        configuration.ShadowDistance.Value = 50f;
        configuration.ShadowMinimumDistance.Value = 25f;
        configuration.ShadowTargetFps.Value = 45f;
        configuration.PipScopeResolutionScale.Value = 1f;
        configuration.AreaLightRefreshFrames.Value = 4;
        configuration.CullingRefreshRate.Value = 30f;
        configuration.DistantShadowRefreshRate.Value = 15f;
        configuration.DeferredDecalRefreshRate.Value = 15f;
        configuration.WeatherRefreshRate.Value = 10f;
        configuration.SoundOnlyShotDistance.Value = 120f;
        configuration.IncomingShotSafetyRadius.Value = 35f;
        configuration.RemoteCombatRecentVisibilityHold.Value = 0.75f;
        configuration.DistantMuzzleEffectDistance.Value = 60f;
        configuration.DistantImpactEffectDistance.Value = 90f;
        configuration.HiddenRemoteLightDistance.Value = 70f;
    }

    private static void ApplyExtreme(PluginConfiguration configuration)
    {
        // Texture mips stay untouched because globally lowering them makes Streets unreadable. The extreme
        // preset instead reduces distance-scaled and transient work that consumes CPU, render time, and VRAM.
        configuration.AggressiveTextureMipLimit.Value = 0;
        configuration.AggressiveShadowDistance.Value = 28f;
        configuration.AggressiveShadowResolution.Value = ShadowResolution.Low;
        configuration.AggressiveShadowCascades.Value = 2;
        configuration.AggressivePixelLights.Value = 0;
        configuration.AggressiveParticleRaycastBudget.Value = 8;
        configuration.AggressiveAmbientReflectionRate.Value = 10f;
        configuration.AggressiveAmbientCommandRate.Value = 12f;
        configuration.RemoteUpdateBudgetInterval.Value = 0.075f;
        configuration.RemoteUpdateBudgetDistance.Value = 20f;
        configuration.RemoteUpdateBudgetHold.Value = 0.05f;
        configuration.RemoteUpdateBudgetDivisor.Value = 8;
        configuration.RemoteAggressivePresentationDistance.Value = 50f;
        configuration.RemoteVisiblePresentationDivisor.Value = 2;
        configuration.ShadowDistance.Value = 35f;
        configuration.ShadowMinimumDistance.Value = 20f;
        configuration.ShadowTargetFps.Value = 60f;
        configuration.PipScopeResolutionScale.Value = 1f;
        configuration.AreaLightRefreshFrames.Value = 8;
        configuration.CullingRefreshRate.Value = 30f;
        configuration.DistantShadowRefreshRate.Value = 15f;
        configuration.DeferredDecalRefreshRate.Value = 15f;
        configuration.WeatherRefreshRate.Value = 10f;
        configuration.SoundOnlyShotDistance.Value = 90f;
        configuration.IncomingShotSafetyRadius.Value = 40f;
        configuration.RemoteCombatRecentVisibilityHold.Value = 0.35f;
        configuration.DistantMuzzleEffectDistance.Value = 40f;
        configuration.DistantImpactEffectDistance.Value = 60f;
        configuration.HiddenRemoteLightDistance.Value = 45f;
        configuration.DistantShellPhysicsDistance.Value = 18f;
        configuration.BulletFlybyAudioRate.Value = 20;
    }
}
