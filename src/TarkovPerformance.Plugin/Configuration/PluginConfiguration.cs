using BepInEx.Configuration;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.Configuration
{
    internal enum PerformancePreset
    {
        Custom,
        Balanced,
        Performance,
        Extreme
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
            Preset = config.Bind("Quick Setup", "PerformancePreset", PerformancePreset.Extreme, "One-click setup for every map and supported game mode. Balanced keeps more visual headroom, Performance applies the full safe optimization stack, Extreme applies the strongest production settings, and Custom preserves manual values.");
            Enabled = config.Bind("General", "Enabled", true, "Master switch for the suite.");
            OptimizationsEnabled = config.Bind("Quick Setup", "AllOptimizationsEnabled", true, "Runtime A/B switch for every behavior-changing optimization. Diagnostics and capture remain active when this is off.");
            OptimizationsToggleKey = config.Bind("Quick Setup", "AllOptimizationsToggleKey", new KeyboardShortcut(KeyCode.Keypad4), "Toggle every optimization on/off together during a raid for a controlled A/B comparison.");
            VerboseLogging = config.Bind("General", "VerboseLogging", false, "Log additional lifecycle details; never logs per frame.");
            OverlayEnabled = config.Bind("Diagnostics", "OverlayEnabled", false, "Show the in-raid diagnostics overlay. Disabled by default for normal play; Num7 toggles it when diagnostics are needed.");
            OverlayDisplayMode = config.Bind("Diagnostics", "OverlayMode", Configuration.OverlayMode.Compact, "Compact is friend-friendly; Detailed shows all profiler counters and experiment status.");
            OverlayKey = config.Bind("Diagnostics", "OverlayKey", new KeyboardShortcut(KeyCode.Keypad7), "Toggle the diagnostics overlay. Also editable in the F12 Configuration Manager.");
            CaptureKey = config.Bind("Diagnostics", "CaptureKey", new KeyboardShortcut(KeyCode.Keypad8), "Toggle continuous back-to-back benchmark captures. Also editable in the F12 Configuration Manager.");
            ContinuousCaptureEnabled = config.Bind("Diagnostics", "ContinuousCapture", false, "Continuously generate back-to-back benchmark and diagnostic reports until toggled off with CaptureKey.");
            DiagnosticReportKey = config.Bind("Diagnostics", "DiagnosticReportKey", new KeyboardShortcut(KeyCode.Keypad9), "Export a diagnostic report with current profiler and method timings.");
            CaptureDurationSeconds = config.Bind("Diagnostics", "CaptureDurationSeconds", 120f, "Capture duration, clamped to 5-900 seconds.");
            ExportCsv = config.Bind("Diagnostics", "ExportCsv", true, "Write the compact CSV benchmark report.");
            ExportJson = config.Bind("Diagnostics", "ExportJson", false, "Also write the much larger JSON report. Disabled by default to avoid unnecessary background formatting and garbage collection during raids.");
            MethodTimingEnabled = config.Bind("Diagnostics", "MethodTimingEnabled", false, "Enable capture-only timing for verified EFT, Fika, audio, and installed-mod frame methods. Disabled by default because it is a developer profiler, not an optimization.");
            MethodTimingCaptureOnly = config.Bind("Diagnostics", "MethodTimingCaptureOnly", true, "Run method timing only during benchmark captures to avoid continuous profiling overhead.");
            BotCounterEnabled = config.Bind("HUD - Bot Counter", "Enabled", false, "Show the suite's cached living-bot counter independently of the Num7 diagnostics panel. Disabled by default for a clean production HUD.");
            BotCounterRefreshRate = config.Bind("HUD - Bot Counter", "RefreshRateHz", 2f, "Counter refresh rate, clamped to 0.5-5 Hz. It reuses the suite entity registry and performs no scene search.");
            BotCounterFontSize = config.Bind("HUD - Bot Counter", "FontSize", 16, "Counter font size, clamped to 10-30.");
            BotCounterFontName = config.Bind("HUD - Bot Counter", "FontName", string.Empty, "Optional installed Windows font name. Leave blank to use EFT's default UI font.");
            BotCounterFontStyle = config.Bind("HUD - Bot Counter", "FontStyle", FontStyle.Bold, "Counter font style.");
            BotCounterShowBossNames = config.Bind("HUD - Bot Counter", "ShowBossNames", true, "Show the names of living named bosses and Goons currently present in the raid.");
            BotCounterHideZeroCategories = config.Bind("HUD - Bot Counter", "OnlyShowSpawnedCategories", true, "Hide a category when none of its living bots are currently spawned.");
            BotCounterOffsetRight = config.Bind("HUD - Bot Counter", "OffsetRight", 20, "Counter distance from the right side of the screen.");
            BotCounterOffsetTop = config.Bind("HUD - Bot Counter", "OffsetTop", 40, "Counter distance from the top of the screen.");
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
            AggressiveShadowResolution = config.Bind("Aggressive", "ShadowResolution", ShadowResolution.Low, "Realtime shadow-map resolution. This reduces shadow VRAM and render work without lowering texture resolution.");
            AggressiveShadowCascades = config.Bind("Aggressive", "ShadowCascades", 2, "Directional shadow cascades. Valid values are 0, 2, or 4; two is the aggressive playable setting.");
            AggressivePixelLights = config.Bind("Aggressive", "PixelLightCount", 1, "Maximum pixel lights, clamped to 0-4.");
            AggressiveParticleRaycastBudget = config.Bind("Aggressive", "ParticleRaycastBudget", 16, "Global particle collision raycast budget, clamped to 0-256.");
            AggressiveAmbientReflectionRate = config.Bind("Aggressive", "AmbientReflectionRefreshHz", 10f, "Limit EFT's custom ambient reflection cubemap refresh rate, clamped to 5-30 Hz. Direct lighting remains full-rate.");
            AggressiveAmbientCommandRate = config.Bind("Aggressive", "AmbientCommandRefreshRateHz", 15f, "Maximum rate for rebuilding ambient/stencil lighting command buffers. Existing commands keep rendering between refreshes; clamped to 8-60 Hz.");
            AreaLightCacheEnabled = config.Bind("Aggressive", "CacheStaticAreaLightCommands", true, "Reuse command buffers for non-shadowed static area lights instead of rebuilding them for every main and optic camera render. HDR and PiP remain enabled.");
            AreaLightRefreshFrames = config.Bind("Aggressive", "AreaLightRefreshFrames", 4, "Refresh cached non-shadowed area-light commands every this many rendered frames, clamped to 1-8. One is vanilla behavior.");
            WorldPresentationBudgetEnabled = config.Bind("Aggressive", "WorldPresentationRateBudget", true, "Rate-limit profiled global culling, distant-shadow, decal and weather visual maintenance. Simulation, networking, damage, audio and input remain full-rate.");
            CullingRefreshRate = config.Bind("Aggressive", "CullingRefreshRateHz", 30f, "Maximum global culling refresh rate, clamped to 20-120 Hz. At 30 Hz newly visible geometry can take up to 33 ms to refresh.");
            DistantShadowRefreshRate = config.Bind("Aggressive", "DistantShadowRefreshRateHz", 15f, "Maximum distant-shadow maintenance rate, clamped to 5-60 Hz.");
            DeferredDecalRefreshRate = config.Bind("Aggressive", "DeferredDecalRefreshRateHz", 15f, "Maximum deferred decal maintenance rate, clamped to 5-60 Hz. Existing impacts remain rendered.");
            WeatherRefreshRate = config.Bind("Aggressive", "WeatherRefreshRateHz", 10f, "Maximum weather presentation update rate, clamped to 5-60 Hz.");
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
            RemoteAggressivePresentationDistance = config.Bind("CPU - Remote Characters", "AggressivePresentationDistance", 50f, "Beyond this distance, visible Fika proxies update arms/late presentation less often and hidden proxies can freeze presentation completely. Clamped to 40-200 metres.");
            RemoteVisiblePresentationDivisor = config.Bind("CPU - Remote Characters", "VisibleDistantUpdateDivisor", 2, "Visible remote arms and late presentation beyond the aggressive distance run once per this many frames. One is full rate; clamped to 1-4.");
            RemoteFreezeHiddenPresentation = config.Bind("CPU - Remote Characters", "FreezeHiddenDistantPresentation", true, "Completely stop arms, body, IK and complex-late presentation for baked-hidden Fika proxies beyond the aggressive distance. Snapshot interpolation, audio and authoritative headless AI continue.");
            UseAllLogicalProcessors = config.Bind("CPU - Threading", "UseAllLogicalProcessors", true, "During raids, remove a process-affinity restriction so Unity and compatible mods may use every logical processor. The original affinity is restored when optimizations are toggled off or the raid ends.");
            FramePacingEnabled = config.Bind("CPU - Frame Pacing", "Enabled", true, "Limit background asset-upload pressure and enable allocation-saving physics callbacks. Does not enable mip streaming.");
            BackgroundLoadingLowPriority = config.Bind("CPU - Frame Pacing", "LowPriorityBackgroundLoading", true, "Keep background loading threads from competing as aggressively with the main game thread.");
            AsyncUploadTimeSlice = config.Bind("CPU - Frame Pacing", "AsyncUploadTimeSliceMs", 2, "Maximum main/render frame time spent on asynchronous uploads, clamped to 1-8 ms.");
            AsyncUploadBufferMb = config.Bind("CPU - Frame Pacing", "AsyncUploadBufferMB", 32, "Persistent asynchronous upload buffer, clamped to 16-128 MiB.");
            AsyncUploadPersistentBuffer = config.Bind("CPU - Frame Pacing", "PersistentUploadBuffer", true, "Reuse the upload buffer to avoid repeated allocation and release work.");
            ReusePhysicsCollisionCallbacks = config.Bind("CPU - Frame Pacing", "ReusePhysicsCollisionCallbacks", true, "Reuse Unity collision callback objects to reduce managed allocations during physics-heavy combat.");
            KnownModFixesEnabled = config.Bind("Compatibility", "FixKnownPeriodicScans", true, "Replace verified periodic full-scene searches in compatible installed mods with the suite's cached GameWorld reference.");
            DynamicMapsOptimizationEnabled = config.Bind("Compatibility - Dynamic Maps", "Enabled", true, "Optimize the installed Dynamic Maps client without removing its real map or minimap.");
            DynamicMapsLeanMarkers = config.Bind("Compatibility - Dynamic Maps", "OnlyPlayerQuestPartyAndKilledBodies", true, "Keep player, party, quests, killed bodies, extracts, doors, transits and dropped backpacks; disable live enemy, scav, boss and unrelated world-event markers.");
            DynamicMapsMiniMapRefreshRate = config.Bind("Compatibility - Dynamic Maps", "MiniMapRefreshRateHz", 24f, "Rate-limit only the expensive always-on player recenter operation, clamped to 15-60 Hz. Map input and the full map remain full-rate.");
            HotPathLogSuppressionEnabled = config.Bind("Compatibility", "SuppressKnownCombatInfoLogs", true, "Suppress only verified per-projectile RealisticFrag informational messages during raids. Warnings and errors are never suppressed.");
            CombatPresentationEnabled = config.Bind("CPU - Combat Presentation", "Enabled", true, "Make hidden distant combat presentation nearly free while preserving gunshot audio, incoming fire, damage, nearby combat, explosives, and PiP visibility.");
            SoundOnlyRemoteShots = config.Bind("CPU - Combat Presentation", "SoundOnlyRemoteShotsOnFikaClients", true, "On a non-host Fika client, do not create a duplicate client bullet for a baked-hidden distant shot whose trajectory cannot approach the local player. The authoritative host still applies damage.");
            SoundOnlyShotDistance = config.Bind("CPU - Combat Presentation", "SoundOnlyDistance", 120f, "Minimum shooter distance for Fika client sound-only shots, clamped to 60-400 metres.");
            IncomingShotSafetyRadius = config.Bind("CPU - Combat Presentation", "IncomingFireSafetyRadius", 35f, "Always keep full bullet simulation when the shot ray passes within this distance of the local player, clamped to 15-75 metres.");
            RemoteCombatRecentVisibilityHold = config.Bind("CPU - Combat Presentation", "RecentlyVisibleHoldSeconds", 0.75f, "Keep full combat presentation briefly after a remote entity leaves visibility, clamped to 0.25-3 seconds.");
            RequireBakedOcclusionForSoundOnly = config.Bind("CPU - Combat Presentation", "RequireBakedMapOcclusion", true, "Only use sound-only mode when EFT's baked map culling proves the shooter is hidden. Recommended for safe production testing.");
            CullDistantMuzzleEffects = config.Bind("CPU - Combat Presentation", "CullHiddenMuzzleEffects", true, "Suppress muzzle flash, smoke, sparks, heat and muzzle lighting for distant hidden remote characters.");
            DistantMuzzleEffectDistance = config.Bind("CPU - Combat Presentation", "MuzzleEffectDistance", 60f, "Keep complete muzzle presentation inside this distance, clamped to 25-200 metres.");
            CullDistantImpactEffects = config.Bind("CPU - Combat Presentation", "CullOffscreenImpactEffects", true, "Suppress distant offscreen bullet impact particles, decals and ricochet presentation after hit processing.");
            DistantImpactEffectDistance = config.Bind("CPU - Combat Presentation", "ImpactEffectDistance", 90f, "Keep all bullet impact presentation inside this distance, clamped to 40-250 metres.");
            CullHiddenRemoteLights = config.Bind("CPU - Combat Presentation", "CullHiddenRemoteLights", true, "Stop hidden distant character lights from contributing to either the main or optic camera while retaining their synchronized state.");
            HiddenRemoteLightDistance = config.Bind("CPU - Combat Presentation", "RemoteLightDistance", 70f, "Keep remote character light rendering inside this distance, clamped to 30-250 metres.");
            CullDistantShellPhysics = config.Bind("CPU - Combat Presentation", "CullDistantCasingPhysics", true, "Skip casing trajectory raycasts and updates when the ejection point is far from the camera.");
            DistantShellPhysicsDistance = config.Bind("CPU - Combat Presentation", "CasingPhysicsDistance", 25f, "Keep full casing physics inside this distance, clamped to 10-100 metres.");
            BudgetBulletFlybyAudio = config.Bind("CPU - Combat Presentation", "BudgetBulletFlybyScans", true, "Limit how often the client scans all active bullets for whizz-by audio. Gunshots and actual ballistics are unchanged.");
            BulletFlybyAudioRate = config.Bind("CPU - Combat Presentation", "BulletFlybyScanRate", 30, "Maximum active-bullet flyby scans per second, clamped to 15-120.");
            PipScopeOptimizationEnabled = config.Bind("CPU - PiP Scopes", "Enabled", true, "Optimize Tarkov's real picture-in-picture camera when the no-PiP replacement is off.");
            PipReplacementEnabled = config.Bind("CPU - PiP Scopes", "ReplacePiPWithMainCameraZoom", true, "Use PiP-Disabler's complete main-camera zoom, reticle and lens-mask implementation instead of leaving a frozen optic texture.");
            PipReplacementToggleKey = config.Bind("CPU - PiP Scopes", "ReplacementToggleKey", new KeyboardShortcut(KeyCode.Keypad3), "Toggle full-resolution vanilla PiP and the main-camera zoom replacement during a raid.");
            PipScopeResolutionScale = config.Bind("CPU - PiP Scopes", "ResolutionScale", 1f, "Vanilla PiP render-texture scale, clamped to 0.35-1.0. Default 1.0 keeps full definition; lower values are an explicit user choice.");
            PipScopeSkipSpecialOptics = config.Bind("CPU - PiP Scopes", "LeaveThermalAndNightVisionUnchanged", true, "Do not resize thermal and night-vision optics.");
            PipScopeDisableMsaa = config.Bind("CPU - PiP Scopes", "DisableMSAA", true, "Disable MSAA on the secondary optic camera while optimized.");
            HeadlessAuthorityEnabled = config.Bind("Headless Authority", "Enabled", true, "On a Fika headless host only, reduce redundant bot snapshot serialization and cap bursty ORBIT navigation work. Has no effect on a playable client.");
            HeadlessBotStateSendRate = config.Bind("Headless Authority", "BotSnapshotRate", 20, new ConfigDescription("Bot movement snapshots per second from the headless host. 20 preserves interpolation while cutting one third of 30 Hz serialization and traffic.", new AcceptableValueRange<int>(10, 30)));
            HeadlessOrbitNavJobsPerFrame = config.Bind("Headless Authority", "OrbitNavJobsPerFrame", 3, new ConfigDescription("Maximum ORBIT NavMesh path calculations completed in one headless frame. Lower values reduce spikes but can delay distant bot path requests.", new AcceptableValueRange<int>(1, 5)));
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
        internal ConfigEntry<bool> ExportJson { get; }
        internal ConfigEntry<bool> MethodTimingEnabled { get; }
        internal ConfigEntry<bool> MethodTimingCaptureOnly { get; }
        internal ConfigEntry<bool> BotCounterEnabled { get; }
        internal ConfigEntry<float> BotCounterRefreshRate { get; }
        internal ConfigEntry<int> BotCounterFontSize { get; }
        internal ConfigEntry<string> BotCounterFontName { get; }
        internal ConfigEntry<FontStyle> BotCounterFontStyle { get; }
        internal ConfigEntry<bool> BotCounterShowBossNames { get; }
        internal ConfigEntry<bool> BotCounterHideZeroCategories { get; }
        internal ConfigEntry<int> BotCounterOffsetRight { get; }
        internal ConfigEntry<int> BotCounterOffsetTop { get; }
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
        internal ConfigEntry<ShadowResolution> AggressiveShadowResolution { get; }
        internal ConfigEntry<int> AggressiveShadowCascades { get; }
        internal ConfigEntry<int> AggressivePixelLights { get; }
        internal ConfigEntry<int> AggressiveParticleRaycastBudget { get; }
        internal ConfigEntry<float> AggressiveAmbientReflectionRate { get; }
        internal ConfigEntry<float> AggressiveAmbientCommandRate { get; }
        internal ConfigEntry<bool> AreaLightCacheEnabled { get; }
        internal ConfigEntry<int> AreaLightRefreshFrames { get; }
        internal ConfigEntry<bool> WorldPresentationBudgetEnabled { get; }
        internal ConfigEntry<float> CullingRefreshRate { get; }
        internal ConfigEntry<float> DistantShadowRefreshRate { get; }
        internal ConfigEntry<float> DeferredDecalRefreshRate { get; }
        internal ConfigEntry<float> WeatherRefreshRate { get; }
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
        internal ConfigEntry<float> RemoteAggressivePresentationDistance { get; }
        internal ConfigEntry<int> RemoteVisiblePresentationDivisor { get; }
        internal ConfigEntry<bool> RemoteFreezeHiddenPresentation { get; }
        internal ConfigEntry<bool> UseAllLogicalProcessors { get; }
        internal ConfigEntry<bool> FramePacingEnabled { get; }
        internal ConfigEntry<bool> BackgroundLoadingLowPriority { get; }
        internal ConfigEntry<int> AsyncUploadTimeSlice { get; }
        internal ConfigEntry<int> AsyncUploadBufferMb { get; }
        internal ConfigEntry<bool> AsyncUploadPersistentBuffer { get; }
        internal ConfigEntry<bool> ReusePhysicsCollisionCallbacks { get; }
        internal ConfigEntry<bool> KnownModFixesEnabled { get; }
        internal ConfigEntry<bool> DynamicMapsOptimizationEnabled { get; }
        internal ConfigEntry<bool> DynamicMapsLeanMarkers { get; }
        internal ConfigEntry<float> DynamicMapsMiniMapRefreshRate { get; }
        internal ConfigEntry<bool> HotPathLogSuppressionEnabled { get; }
        internal ConfigEntry<bool> CombatPresentationEnabled { get; }
        internal ConfigEntry<bool> SoundOnlyRemoteShots { get; }
        internal ConfigEntry<float> SoundOnlyShotDistance { get; }
        internal ConfigEntry<float> IncomingShotSafetyRadius { get; }
        internal ConfigEntry<float> RemoteCombatRecentVisibilityHold { get; }
        internal ConfigEntry<bool> RequireBakedOcclusionForSoundOnly { get; }
        internal ConfigEntry<bool> CullDistantMuzzleEffects { get; }
        internal ConfigEntry<float> DistantMuzzleEffectDistance { get; }
        internal ConfigEntry<bool> CullDistantImpactEffects { get; }
        internal ConfigEntry<float> DistantImpactEffectDistance { get; }
        internal ConfigEntry<bool> CullHiddenRemoteLights { get; }
        internal ConfigEntry<float> HiddenRemoteLightDistance { get; }
        internal ConfigEntry<bool> CullDistantShellPhysics { get; }
        internal ConfigEntry<float> DistantShellPhysicsDistance { get; }
        internal ConfigEntry<bool> BudgetBulletFlybyAudio { get; }
        internal ConfigEntry<int> BulletFlybyAudioRate { get; }
        internal ConfigEntry<bool> PipScopeOptimizationEnabled { get; }
        internal ConfigEntry<bool> PipReplacementEnabled { get; }
        internal ConfigEntry<KeyboardShortcut> PipReplacementToggleKey { get; }
        internal ConfigEntry<float> PipScopeResolutionScale { get; }
        internal ConfigEntry<bool> PipScopeSkipSpecialOptics { get; }
        internal ConfigEntry<bool> PipScopeDisableMsaa { get; }
        internal ConfigEntry<bool> HeadlessAuthorityEnabled { get; }
        internal ConfigEntry<int> HeadlessBotStateSendRate { get; }
        internal ConfigEntry<int> HeadlessOrbitNavJobsPerFrame { get; }

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
