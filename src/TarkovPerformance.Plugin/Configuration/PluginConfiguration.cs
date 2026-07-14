using BepInEx.Configuration;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.Configuration
{
    internal enum PerformancePreset
    {
        Custom,
        Balanced,
        OldCpuAggressive
    }

    internal enum OverlayMode
    {
        Compact,
        Detailed
    }

    internal sealed class PluginConfiguration
    {
        internal PluginConfiguration(ConfigFile config)
        {
            Preset = config.Bind("Quick Setup", "PerformancePreset", PerformancePreset.OldCpuAggressive, "One-click setup. OldCpuAggressive targets 4-core/8-thread CPUs without changing LOD. Choose Custom to control every switch yourself.");
            Enabled = config.Bind("General", "Enabled", true, "Master switch for the suite.");
            OptimizationsEnabled = config.Bind("Quick Setup", "AllOptimizationsEnabled", true, "Runtime A/B switch for every behavior-changing optimization. Diagnostics and capture remain active when this is off.");
            OptimizationsToggleKey = config.Bind("Quick Setup", "AllOptimizationsToggleKey", new KeyboardShortcut(KeyCode.Keypad4), "Toggle every optimization on/off together during a raid for a controlled A/B comparison.");
            VerboseLogging = config.Bind("General", "VerboseLogging", false, "Log additional lifecycle details; never logs per frame.");
            OverlayEnabled = config.Bind("Diagnostics", "OverlayEnabled", true, "Show the in-raid diagnostics overlay.");
            OverlayDisplayMode = config.Bind("Diagnostics", "OverlayMode", Configuration.OverlayMode.Compact, "Compact is friend-friendly; Detailed shows all profiler counters and experiment status.");
            OverlayKey = config.Bind("Diagnostics", "OverlayKey", new KeyboardShortcut(KeyCode.Keypad7), "Toggle the diagnostics overlay. Also editable in the F12 Configuration Manager.");
            CaptureKey = config.Bind("Diagnostics", "CaptureKey", new KeyboardShortcut(KeyCode.Keypad8), "Toggle continuous back-to-back benchmark captures. Also editable in the F12 Configuration Manager.");
            ContinuousCaptureEnabled = config.Bind("Diagnostics", "ContinuousCapture", false, "Continuously generate back-to-back benchmark and diagnostic reports until toggled off with CaptureKey.");
            DiagnosticReportKey = config.Bind("Diagnostics", "DiagnosticReportKey", new KeyboardShortcut(KeyCode.Keypad9), "Export a diagnostic report with current profiler and method timings.");
            CaptureDurationSeconds = config.Bind("Diagnostics", "CaptureDurationSeconds", 120f, "Capture duration, clamped to 5-900 seconds.");
            ExportCsv = config.Bind("Diagnostics", "ExportCsv", true, "Write CSV in addition to JSON.");
            MethodTimingEnabled = config.Bind("Diagnostics", "MethodTimingEnabled", true, "Enable capture-only timing for verified EFT, Fika, audio, and installed-mod frame methods. This reveals milliseconds that Unity's release profiler markers do not expose.");
            MethodTimingCaptureOnly = config.Bind("Diagnostics", "MethodTimingCaptureOnly", true, "Run method timing only during benchmark captures to avoid continuous profiling overhead.");
            ShadowEnabled = config.Bind("Experiments", "RemoteCharacterShadowsEnabled", true, "Disable distant confirmed remote-AI shadow casting.");
            ShadowDistance = config.Bind("Experiments", "RemoteCharacterShadowDistance", 120f, "Distance in metres, clamped to 20-1000.");
            ShadowUpdateInterval = config.Bind("Experiments", "RemoteCharacterShadowUpdateIntervalSeconds", 0.25f, "Time-based update interval, clamped to 0.1-5 seconds.");
            ShadowToggleKey = config.Bind("Experiments", "RemoteCharacterShadowToggleKey", new KeyboardShortcut(KeyCode.Keypad6), "Toggle the shadow experiment. The Boolean toggle is also available in the F12 menu.");
            ShadowAdaptiveEnabled = config.Bind("Experiments", "RemoteCharacterShadowsAdaptive", true, "When the shadow experiment is enabled, gradually reduce its distance during sustained CPU pressure and recover slowly.");
            ShadowMinimumDistance = config.Bind("Experiments", "RemoteCharacterShadowMinimumDistance", 60f, "Closest adaptive shadow cutoff in metres, clamped to 20 and the normal shadow distance.");
            ShadowTargetFps = config.Bind("Experiments", "RemoteCharacterShadowTargetFps", 60f, "Adaptive shadow target, clamped to 20-240 FPS.");
            SkinningEnabled = config.Bind("Experiments", "RemoteAiOffscreenSkinningEnabled", true, "Stop explicitly requested offscreen skinning for distant confirmed remote AI.");
            SkinningDistance = config.Bind("Experiments", "RemoteAiOffscreenSkinningDistance", 80f, "Minimum distance for the offscreen skinning guard, clamped to 20-1000 metres.");
            SkinningUpdateInterval = config.Bind("Experiments", "RemoteAiOffscreenSkinningUpdateIntervalSeconds", 0.1f, "Skinning guard update interval, clamped to 0.05-5 seconds.");
            SkinningOffscreenHold = config.Bind("Experiments", "RemoteAiOffscreenSkinningHoldSeconds", 0.5f, "How long a distant AI must stay invisible before changing offscreen skinning, clamped to 0.1-10 seconds.");
            SkinningToggleKey = config.Bind("Experiments", "RemoteAiOffscreenSkinningToggleKey", new KeyboardShortcut(KeyCode.Keypad5), "Toggle the offscreen skinning experiment. Also available in the F12 menu.");
            AggressiveModeEnabled = config.Bind("Aggressive", "Enabled", true, "Apply the aggressive global render profile. This intentionally trades visual quality for CPU, render-thread, and VRAM headroom.");
            AggressiveTextureMipLimit = config.Bind("Aggressive", "TextureMipLimit", 1, "Highest texture mip levels to skip, clamped to 0-2. Apply before loading a raid to avoid a mid-raid texture re-upload stall.");
            AggressiveShadowDistance = config.Bind("Aggressive", "ShadowDistance", 45f, "Global shadow distance in metres, clamped to 0-150.");
            AggressivePixelLights = config.Bind("Aggressive", "PixelLightCount", 1, "Maximum pixel lights, clamped to 0-4.");
            AggressiveParticleRaycastBudget = config.Bind("Aggressive", "ParticleRaycastBudget", 16, "Global particle collision raycast budget, clamped to 0-256.");
            CosmeticDeclutterEnabled = config.Bind("Aggressive", "CosmeticDeclutterEnabled", true, "Incrementally hide small renderer-only cosmetic clutter selected by a conservative name and size filter.");
            CosmeticDeclutterBatchSize = config.Bind("Aggressive", "CosmeticDeclutterBatchSize", 200, "Renderers classified per frame after raid start, clamped to 25-1000.");
            RemoteUpdateBudgetEnabled = config.Bind("CPU - Remote Characters", "Enabled", true, "Use EFT/Fika's existing visibility result to reduce presentation-only work for hidden remote characters.");
            RemoteUpdateBudgetDistance = config.Bind("CPU - Remote Characters", "MinimumDistance", 40f, "Never budget a hidden remote character closer than this distance, clamped to 20-200 metres.");
            RemoteUpdateBudgetHold = config.Bind("CPU - Remote Characters", "HiddenHoldSeconds", 0.2f, "How long a character must remain hidden before budgeting it, clamped to 0.05-2 seconds.");
            RemoteUpdateBudgetInterval = config.Bind("CPU - Remote Characters", "ScanIntervalSeconds", 0.05f, "Visibility cache refresh interval, clamped to 0.033-0.5 seconds.");
            RemoteUpdateBudgetDivisor = config.Bind("CPU - Remote Characters", "HiddenUpdateDivisor", 4, "Hidden prop and trigger-search work runs once per this many eligible calls, clamped to 2-8.");
            RemoteAnimatorCullingEnabled = config.Bind("CPU - Remote Characters", "CullHiddenAnimators", true, "Use Unity CullCompletely for animators belonging to confirmed hidden remote characters; restored immediately when visible.");
            RemotePresentationBudgetEnabled = config.Bind("CPU - Remote Characters", "BudgetHiddenArmsBodyAndIK", true, "Also reduce arms, body, and inverse-kinematics presentation updates for confirmed hidden distant characters. Network state and visibility updates are never skipped.");
            RemoteComplexLateUpdateBudgetEnabled = config.Bind("CPU - Remote Characters", "BudgetHiddenComplexLateUpdate", true, "Aggressively reduce the outer late-presentation pass for confirmed hidden distant characters. Visibility and network update passes are never skipped.");
            UseAllLogicalProcessors = config.Bind("CPU - Threading", "UseAllLogicalProcessors", true, "During raids, remove a process-affinity restriction so Unity and compatible mods may use every logical processor. The original affinity is restored when optimizations are toggled off or the raid ends.");
            FramePacingEnabled = config.Bind("CPU - Frame Pacing", "Enabled", true, "Limit background asset-upload pressure and enable allocation-saving physics callbacks. Does not enable mip streaming.");
            BackgroundLoadingLowPriority = config.Bind("CPU - Frame Pacing", "LowPriorityBackgroundLoading", true, "Keep background loading threads from competing as aggressively with the main game thread.");
            AsyncUploadTimeSlice = config.Bind("CPU - Frame Pacing", "AsyncUploadTimeSliceMs", 2, "Maximum main/render frame time spent on asynchronous uploads, clamped to 1-8 ms.");
            AsyncUploadBufferMb = config.Bind("CPU - Frame Pacing", "AsyncUploadBufferMB", 32, "Persistent asynchronous upload buffer, clamped to 16-128 MiB.");
            AsyncUploadPersistentBuffer = config.Bind("CPU - Frame Pacing", "PersistentUploadBuffer", true, "Reuse the upload buffer to avoid repeated allocation and release work.");
            ReusePhysicsCollisionCallbacks = config.Bind("CPU - Frame Pacing", "ReusePhysicsCollisionCallbacks", true, "Reuse Unity collision callback objects to reduce managed allocations during physics-heavy combat.");
            KnownModFixesEnabled = config.Bind("Compatibility", "FixKnownPeriodicScans", true, "Replace verified periodic full-scene searches in compatible installed mods with the suite's cached GameWorld reference.");
            HotPathLogSuppressionEnabled = config.Bind("Compatibility", "SuppressKnownCombatInfoLogs", true, "Suppress only verified per-projectile RealisticFrag informational messages during raids. Warnings and errors are never suppressed.");
            CombatPresentationEnabled = config.Bind("CPU - Combat Presentation", "Enabled", true, "Reduce client-only distant gunfire cosmetics without changing ballistics, damage, networking, or nearby effects.");
            CullDistantShellPhysics = config.Bind("CPU - Combat Presentation", "CullDistantCasingPhysics", true, "Skip casing trajectory raycasts and updates when the ejection point is far from the camera.");
            DistantShellPhysicsDistance = config.Bind("CPU - Combat Presentation", "CasingPhysicsDistance", 25f, "Keep full casing physics inside this distance, clamped to 10-100 metres.");
            BudgetBulletFlybyAudio = config.Bind("CPU - Combat Presentation", "BudgetBulletFlybyScans", true, "Limit how often the client scans all active bullets for whizz-by audio. Gunshots and actual ballistics are unchanged.");
            BulletFlybyAudioRate = config.Bind("CPU - Combat Presentation", "BulletFlybyScanRate", 30, "Maximum active-bullet flyby scans per second, clamped to 15-120.");
            PipScopeOptimizationEnabled = config.Bind("CPU - PiP Scopes", "Enabled", true, "Keep real picture-in-picture scopes but reduce the second camera's render cost.");
            PipScopeResolutionScale = config.Bind("CPU - PiP Scopes", "ResolutionScale", 0.5f, "Internal optic render-texture scale, clamped to 0.35-1.0. This does not remove PiP.");
            PipScopeSkipSpecialOptics = config.Bind("CPU - PiP Scopes", "LeaveThermalAndNightVisionUnchanged", true, "Do not resize thermal and night-vision optics.");
            PipScopeDisableMsaa = config.Bind("CPU - PiP Scopes", "DisableMSAA", true, "Disable MSAA on the secondary optic camera while optimized.");
        }

        internal ConfigEntry<PerformancePreset> Preset { get; }
        internal ConfigEntry<bool> Enabled { get; }
        internal ConfigEntry<bool> OptimizationsEnabled { get; }
        internal ConfigEntry<KeyboardShortcut> OptimizationsToggleKey { get; }
        internal ConfigEntry<bool> VerboseLogging { get; }
        internal ConfigEntry<bool> OverlayEnabled { get; }
        internal ConfigEntry<OverlayMode> OverlayDisplayMode { get; }
        internal ConfigEntry<KeyboardShortcut> OverlayKey { get; }
        internal ConfigEntry<KeyboardShortcut> CaptureKey { get; }
        internal ConfigEntry<bool> ContinuousCaptureEnabled { get; }
        internal ConfigEntry<KeyboardShortcut> DiagnosticReportKey { get; }
        internal ConfigEntry<float> CaptureDurationSeconds { get; }
        internal ConfigEntry<bool> ExportCsv { get; }
        internal ConfigEntry<bool> MethodTimingEnabled { get; }
        internal ConfigEntry<bool> MethodTimingCaptureOnly { get; }
        internal ConfigEntry<bool> ShadowEnabled { get; }
        internal ConfigEntry<float> ShadowDistance { get; }
        internal ConfigEntry<float> ShadowUpdateInterval { get; }
        internal ConfigEntry<KeyboardShortcut> ShadowToggleKey { get; }
        internal ConfigEntry<bool> ShadowAdaptiveEnabled { get; }
        internal ConfigEntry<float> ShadowMinimumDistance { get; }
        internal ConfigEntry<float> ShadowTargetFps { get; }
        internal ConfigEntry<bool> SkinningEnabled { get; }
        internal ConfigEntry<float> SkinningDistance { get; }
        internal ConfigEntry<float> SkinningUpdateInterval { get; }
        internal ConfigEntry<float> SkinningOffscreenHold { get; }
        internal ConfigEntry<KeyboardShortcut> SkinningToggleKey { get; }
        internal ConfigEntry<bool> AggressiveModeEnabled { get; }
        internal ConfigEntry<int> AggressiveTextureMipLimit { get; }
        internal ConfigEntry<float> AggressiveShadowDistance { get; }
        internal ConfigEntry<int> AggressivePixelLights { get; }
        internal ConfigEntry<int> AggressiveParticleRaycastBudget { get; }
        internal ConfigEntry<bool> CosmeticDeclutterEnabled { get; }
        internal ConfigEntry<int> CosmeticDeclutterBatchSize { get; }
        internal ConfigEntry<bool> RemoteUpdateBudgetEnabled { get; }
        internal ConfigEntry<float> RemoteUpdateBudgetDistance { get; }
        internal ConfigEntry<float> RemoteUpdateBudgetHold { get; }
        internal ConfigEntry<float> RemoteUpdateBudgetInterval { get; }
        internal ConfigEntry<int> RemoteUpdateBudgetDivisor { get; }
        internal ConfigEntry<bool> RemoteAnimatorCullingEnabled { get; }
        internal ConfigEntry<bool> RemotePresentationBudgetEnabled { get; }
        internal ConfigEntry<bool> RemoteComplexLateUpdateBudgetEnabled { get; }
        internal ConfigEntry<bool> UseAllLogicalProcessors { get; }
        internal ConfigEntry<bool> FramePacingEnabled { get; }
        internal ConfigEntry<bool> BackgroundLoadingLowPriority { get; }
        internal ConfigEntry<int> AsyncUploadTimeSlice { get; }
        internal ConfigEntry<int> AsyncUploadBufferMb { get; }
        internal ConfigEntry<bool> AsyncUploadPersistentBuffer { get; }
        internal ConfigEntry<bool> ReusePhysicsCollisionCallbacks { get; }
        internal ConfigEntry<bool> KnownModFixesEnabled { get; }
        internal ConfigEntry<bool> HotPathLogSuppressionEnabled { get; }
        internal ConfigEntry<bool> CombatPresentationEnabled { get; }
        internal ConfigEntry<bool> CullDistantShellPhysics { get; }
        internal ConfigEntry<float> DistantShellPhysicsDistance { get; }
        internal ConfigEntry<bool> BudgetBulletFlybyAudio { get; }
        internal ConfigEntry<int> BulletFlybyAudioRate { get; }
        internal ConfigEntry<bool> PipScopeOptimizationEnabled { get; }
        internal ConfigEntry<float> PipScopeResolutionScale { get; }
        internal ConfigEntry<bool> PipScopeSkipSpecialOptics { get; }
        internal ConfigEntry<bool> PipScopeDisableMsaa { get; }

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
