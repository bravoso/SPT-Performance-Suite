using BepInEx.Configuration;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.Configuration
{
    internal sealed class PluginConfiguration
    {
        internal PluginConfiguration(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Master switch for the suite.");
            VerboseLogging = config.Bind("General", "VerboseLogging", false, "Log additional lifecycle details; never logs per frame.");
            OverlayEnabled = config.Bind("Diagnostics", "OverlayEnabled", true, "Show the in-raid diagnostics overlay.");
            OverlayKey = config.Bind("Diagnostics", "OverlayKey", new KeyboardShortcut(KeyCode.Keypad7), "Toggle the diagnostics overlay. Also editable in the F12 Configuration Manager.");
            CaptureKey = config.Bind("Diagnostics", "CaptureKey", new KeyboardShortcut(KeyCode.Keypad8), "Start a benchmark capture. Also editable in the F12 Configuration Manager.");
            DiagnosticReportKey = config.Bind("Diagnostics", "DiagnosticReportKey", new KeyboardShortcut(KeyCode.Keypad9), "Export a diagnostic report with current profiler and method timings.");
            CaptureDurationSeconds = config.Bind("Diagnostics", "CaptureDurationSeconds", 120f, "Capture duration, clamped to 5-900 seconds.");
            ExportCsv = config.Bind("Diagnostics", "ExportCsv", true, "Write CSV in addition to JSON.");
            MethodTimingEnabled = config.Bind("Diagnostics", "MethodTimingEnabled", false, "Enable diagnostics-only Harmony method timing. Can be changed live from the F12 menu.");
            MethodTimingCaptureOnly = config.Bind("Diagnostics", "MethodTimingCaptureOnly", true, "Run method timing only during benchmark captures to avoid continuous profiling overhead.");
            ShadowEnabled = config.Bind("Experiments", "RemoteCharacterShadowsEnabled", false, "Disable distant confirmed remote-AI shadow casting. Disabled by default.");
            ShadowDistance = config.Bind("Experiments", "RemoteCharacterShadowDistance", 120f, "Distance in metres, clamped to 20-1000.");
            ShadowUpdateInterval = config.Bind("Experiments", "RemoteCharacterShadowUpdateIntervalSeconds", 0.25f, "Time-based update interval, clamped to 0.1-5 seconds.");
            ShadowToggleKey = config.Bind("Experiments", "RemoteCharacterShadowToggleKey", new KeyboardShortcut(KeyCode.Keypad6), "Toggle the shadow experiment. The Boolean toggle is also available in the F12 menu.");
            ShadowDryRun = config.Bind("Experiments", "RemoteCharacterShadowsDryRun", false, "Report changes without modifying renderers.");
            ShadowAdaptiveEnabled = config.Bind("Experiments", "RemoteCharacterShadowsAdaptive", true, "When the shadow experiment is enabled, gradually reduce its distance during sustained CPU pressure and recover slowly.");
            ShadowMinimumDistance = config.Bind("Experiments", "RemoteCharacterShadowMinimumDistance", 60f, "Closest adaptive shadow cutoff in metres, clamped to 20 and the normal shadow distance.");
            ShadowTargetFps = config.Bind("Experiments", "RemoteCharacterShadowTargetFps", 60f, "Adaptive shadow target, clamped to 20-240 FPS.");
            SkinningEnabled = config.Bind("Experiments", "RemoteAiOffscreenSkinningEnabled", false, "Stop explicitly requested offscreen skinning for distant confirmed remote AI. Experimental and disabled by default.");
            SkinningDistance = config.Bind("Experiments", "RemoteAiOffscreenSkinningDistance", 80f, "Minimum distance for the offscreen skinning guard, clamped to 20-1000 metres.");
            SkinningUpdateInterval = config.Bind("Experiments", "RemoteAiOffscreenSkinningUpdateIntervalSeconds", 0.1f, "Skinning guard update interval, clamped to 0.05-5 seconds.");
            SkinningOffscreenHold = config.Bind("Experiments", "RemoteAiOffscreenSkinningHoldSeconds", 0.5f, "How long a distant AI must stay invisible before changing offscreen skinning, clamped to 0.1-10 seconds.");
            SkinningToggleKey = config.Bind("Experiments", "RemoteAiOffscreenSkinningToggleKey", new KeyboardShortcut(KeyCode.Keypad5), "Toggle the offscreen skinning experiment. Also available in the F12 menu.");
            SkinningDryRun = config.Bind("Experiments", "RemoteAiOffscreenSkinningDryRun", false, "Count potential offscreen skinning changes without applying them.");
            AggressiveModeEnabled = config.Bind("Aggressive", "Enabled", false, "Apply the aggressive global render profile. This intentionally trades visual quality for CPU, render-thread, and VRAM headroom.");
            AggressiveMaximumLodLevel = config.Bind("Aggressive", "MaximumLodLevel", 1, "Skip this many highest scene LOD levels, clamped to 0-3.");
            AggressiveLodBias = config.Bind("Aggressive", "LodBias", 0.6f, "Global LOD bias, clamped to 0.25-2.0. Lower values select cheaper LODs sooner.");
            AggressiveTextureMipLimit = config.Bind("Aggressive", "TextureMipLimit", 1, "Highest texture mip levels to skip, clamped to 0-2. Apply before loading a raid to avoid a mid-raid texture re-upload stall.");
            AggressiveShadowDistance = config.Bind("Aggressive", "ShadowDistance", 45f, "Global shadow distance in metres, clamped to 0-150.");
            AggressivePixelLights = config.Bind("Aggressive", "PixelLightCount", 1, "Maximum pixel lights, clamped to 0-4.");
            AggressiveParticleRaycastBudget = config.Bind("Aggressive", "ParticleRaycastBudget", 16, "Global particle collision raycast budget, clamped to 0-256.");
            RemoteAiRenderLodEnabled = config.Bind("Aggressive", "RemoteAiRenderLodEnabled", false, "Force cheaper LOD, skinning, reflection, and motion-vector settings on distant confirmed remote AI.");
            RemoteAiRenderLodNearDistance = config.Bind("Aggressive", "RemoteAiRenderLodNearDistance", 25f, "Inside this distance remote AI retain automatic full-quality rendering, clamped to 10-200 metres.");
            RemoteAiRenderLodFarDistance = config.Bind("Aggressive", "RemoteAiRenderLodFarDistance", 60f, "Beyond this distance remote AI use their cheapest available LOD, clamped to the near distance and 500 metres.");
            RemoteAiRenderLodUpdateInterval = config.Bind("Aggressive", "RemoteAiRenderLodUpdateIntervalSeconds", 0.1f, "Remote render LOD refresh interval, clamped to 0.05-2 seconds.");
            RemoteAiRenderLodDryRun = config.Bind("Aggressive", "RemoteAiRenderLodDryRun", false, "Count remote render LOD candidates without changing components.");
            CosmeticDeclutterEnabled = config.Bind("Aggressive", "CosmeticDeclutterEnabled", false, "Incrementally hide small renderer-only cosmetic clutter selected by a conservative name and size filter.");
            CosmeticDeclutterDryRun = config.Bind("Aggressive", "CosmeticDeclutterDryRun", false, "Count cosmetic clutter candidates without hiding them.");
            CosmeticDeclutterBatchSize = config.Bind("Aggressive", "CosmeticDeclutterBatchSize", 200, "Renderers classified per frame after raid start, clamped to 25-1000.");
        }

        internal ConfigEntry<bool> Enabled { get; }
        internal ConfigEntry<bool> VerboseLogging { get; }
        internal ConfigEntry<bool> OverlayEnabled { get; }
        internal ConfigEntry<KeyboardShortcut> OverlayKey { get; }
        internal ConfigEntry<KeyboardShortcut> CaptureKey { get; }
        internal ConfigEntry<KeyboardShortcut> DiagnosticReportKey { get; }
        internal ConfigEntry<float> CaptureDurationSeconds { get; }
        internal ConfigEntry<bool> ExportCsv { get; }
        internal ConfigEntry<bool> MethodTimingEnabled { get; }
        internal ConfigEntry<bool> MethodTimingCaptureOnly { get; }
        internal ConfigEntry<bool> ShadowEnabled { get; }
        internal ConfigEntry<float> ShadowDistance { get; }
        internal ConfigEntry<float> ShadowUpdateInterval { get; }
        internal ConfigEntry<KeyboardShortcut> ShadowToggleKey { get; }
        internal ConfigEntry<bool> ShadowDryRun { get; }
        internal ConfigEntry<bool> ShadowAdaptiveEnabled { get; }
        internal ConfigEntry<float> ShadowMinimumDistance { get; }
        internal ConfigEntry<float> ShadowTargetFps { get; }
        internal ConfigEntry<bool> SkinningEnabled { get; }
        internal ConfigEntry<float> SkinningDistance { get; }
        internal ConfigEntry<float> SkinningUpdateInterval { get; }
        internal ConfigEntry<float> SkinningOffscreenHold { get; }
        internal ConfigEntry<KeyboardShortcut> SkinningToggleKey { get; }
        internal ConfigEntry<bool> SkinningDryRun { get; }
        internal ConfigEntry<bool> AggressiveModeEnabled { get; }
        internal ConfigEntry<int> AggressiveMaximumLodLevel { get; }
        internal ConfigEntry<float> AggressiveLodBias { get; }
        internal ConfigEntry<int> AggressiveTextureMipLimit { get; }
        internal ConfigEntry<float> AggressiveShadowDistance { get; }
        internal ConfigEntry<int> AggressivePixelLights { get; }
        internal ConfigEntry<int> AggressiveParticleRaycastBudget { get; }
        internal ConfigEntry<bool> RemoteAiRenderLodEnabled { get; }
        internal ConfigEntry<float> RemoteAiRenderLodNearDistance { get; }
        internal ConfigEntry<float> RemoteAiRenderLodFarDistance { get; }
        internal ConfigEntry<float> RemoteAiRenderLodUpdateInterval { get; }
        internal ConfigEntry<bool> RemoteAiRenderLodDryRun { get; }
        internal ConfigEntry<bool> CosmeticDeclutterEnabled { get; }
        internal ConfigEntry<bool> CosmeticDeclutterDryRun { get; }
        internal ConfigEntry<int> CosmeticDeclutterBatchSize { get; }

        internal ValidatedConfiguration Validated => ConfigurationValidator.Validate(
            CaptureDurationSeconds.Value,
            ShadowDistance.Value,
            ShadowUpdateInterval.Value,
            ShadowMinimumDistance.Value,
            ShadowTargetFps.Value,
            SkinningDistance.Value,
            SkinningUpdateInterval.Value,
            SkinningOffscreenHold.Value);
    }
}
