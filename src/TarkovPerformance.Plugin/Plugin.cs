using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.FikaAdapter;
using TarkovPerformanceSuite.RuntimeDiagnostics;
using TarkovPerformanceSuite.RuntimeFeatures;
using UnityEngine;

namespace TarkovPerformanceSuite
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lucaswilluweit.tarkovperformancesuite";
        public const string PluginName = "Tarkov Performance Suite";
        public const string PluginVersion = "1.0.0";

        private PluginConfiguration _configuration;
        private RuntimeInformation _runtime;
        private readonly RaidLifecycle _lifecycle = new RaidLifecycle();
        private readonly EntityRegistry _entities = new EntityRegistry();
        private readonly FikaDiagnosticsAdapter _fika = new FikaDiagnosticsAdapter();
        private readonly RecentExceptionLog _exceptions = new RecentExceptionLog();
        private ProfilerMetrics _metrics;
        private DiagnosticsOverlay _overlay;
        private BenchmarkRecorder _benchmark;
        private MethodTimingFramework _timing;
        private ProcessThreadSampler _processThreads;
        private RemoteCharacterShadowFeature _shadows;
        private RemoteAiOffscreenSkinningFeature _skinning;
        private AggressiveQualityFeature _aggressiveQuality;
        private AreaLightCommandCacheFeature _areaLights;
        private WorldPresentationRateFeature _worldPresentation;
        private CosmeticDeclutterFeature _declutter;
        private RemoteUpdateBudgetFeature _remoteBudget;
        private CpuThreadingFeature _cpuThreading;
        private FramePacingFeature _framePacing;
        private KnownModCompatibilityFeature _compatibility;
        private DynamicMapsCompatibilityFeature _dynamicMaps;
        private OptimizedBotCounterFeature _botCounter;
        private HotPathLogSuppressionFeature _hotLogs;
        private CombatPresentationBudgetFeature _combatPresentation;
        private PipScopeOptimizationFeature _pipScopes;
        private HeadlessAuthorityFeature _headlessAuthority;
        private EntityCounts _counts;
        private double _suiteAverageMs;
        private string _outputRoot;
        private float _nextPluginErrorLog;
        private PerformancePreset _lastPreset = (PerformancePreset)(-1);
        private bool _configurationChanged;
        private bool _refreshAggressiveQuality;
        private bool _applyingPreset;
        private bool _profilerMetricsActive;
        private bool _availableProfilerMetricsWrittenForRaid;

        private void Awake()
        {
            Config.SaveOnConfigSet = true;
            _configuration = new PluginConfiguration(Config);
            Config.SettingChanged += OnConfigurationChanged;
            ApplyPresetIfChanged();
            _runtime = RuntimeInformation.Detect();
            _metrics = new ProfilerMetrics(Logger);
            _overlay = new DiagnosticsOverlay { Visible = _configuration.OverlayEnabled.Value };
            _benchmark = new BenchmarkRecorder(Logger);
            _timing = new MethodTimingFramework(Logger);
            _processThreads = new ProcessThreadSampler();
            _shadows = new RemoteCharacterShadowFeature(Logger, _configuration, _entities, _exceptions);
            _skinning = new RemoteAiOffscreenSkinningFeature(Logger, _configuration, _entities, _exceptions);
            _aggressiveQuality = new AggressiveQualityFeature(Logger, _configuration, _exceptions);
            _areaLights = new AreaLightCommandCacheFeature(Logger, _configuration, _exceptions);
            _worldPresentation = new WorldPresentationRateFeature(Logger, _configuration, _exceptions);
            _declutter = new CosmeticDeclutterFeature(Logger, _configuration, _exceptions);
            _remoteBudget = new RemoteUpdateBudgetFeature(Logger, _configuration, _entities, _exceptions);
            _cpuThreading = new CpuThreadingFeature(Logger, _configuration, _exceptions);
            _framePacing = new FramePacingFeature(Logger, _configuration, _exceptions);
            _compatibility = new KnownModCompatibilityFeature(Logger, _configuration, _exceptions);
            _dynamicMaps = new DynamicMapsCompatibilityFeature(Logger, _configuration, _exceptions);
            _botCounter = new OptimizedBotCounterFeature(Logger, _configuration, _entities);
            _hotLogs = new HotPathLogSuppressionFeature(Logger, _configuration, _exceptions);
            _combatPresentation = new CombatPresentationBudgetFeature(Logger, _configuration, _entities, _exceptions);
            _pipScopes = new PipScopeOptimizationFeature(Logger, _configuration, _exceptions);
            _headlessAuthority = new HeadlessAuthorityFeature(Logger, _configuration, _exceptions);
            _outputRoot = Path.Combine(BepInEx.Paths.PluginPath, "TarkovPerformanceSuite");
            _lifecycle.RaidStarted += OnRaidStarted;
            _lifecycle.RaidEnded += OnRaidEnded;
            _lifecycle.StateChanged += state => { if (_configuration.VerboseLogging.Value) Logger.LogInfo("Lifecycle state: " + state); };
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. The Extreme production preset is enabled by default; diagnostics and HUD overlays remain closed until requested.");
            _runtime.Log(Logger);
            _shadows.Initialize();
            _skinning.Initialize();
            _aggressiveQuality.Initialize();
            _areaLights.Initialize();
            _worldPresentation.Initialize();
            _declutter.Initialize();
            _remoteBudget.Initialize();
            _cpuThreading.Initialize();
            _framePacing.Initialize();
            _compatibility.Initialize();
            _dynamicMaps.Initialize();
            _botCounter.Initialize();
            _hotLogs.Initialize();
            _combatPresentation.Initialize();
            _pipScopes.Initialize();
            _headlessAuthority.Initialize();
        }

        private void Update()
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                _benchmark.DrainCompletionLog();
                _timing.FrameBoundary(Time.realtimeSinceStartup);
                ApplyPresetIfChanged();
                ApplyDynamicConfiguration();
                if (!_configuration.Enabled.Value)
                {
                    _timing.SetRuntimeEnabled(false);
                    SyncOptimizationFeatures(false);
                    return;
                }
                float now = Time.realtimeSinceStartup;
                _lifecycle.Tick(now);

                if (_configuration.OverlayKey.Value.IsDown())
                {
                    _overlay.Visible = !_overlay.Visible;
                    _configuration.OverlayEnabled.Value = _overlay.Visible;
                }

                if (_configuration.OptimizationsToggleKey.Value.IsDown())
                {
                    _configuration.OptimizationsEnabled.Value = !_configuration.OptimizationsEnabled.Value;
                    Logger.LogWarning("A/B state changed: ALL OPTIMIZATIONS " + (_configuration.OptimizationsEnabled.Value ? "ON" : "OFF")
                        + ". Allow several seconds for texture and frame-time transients to settle before comparing.");
                    _overlay.Reset();
                }

                if (_configuration.DiagnosticReportKey.Value.IsDown())
                {
                    bool keepMetrics = _overlay.Visible || _configuration.ContinuousCaptureEnabled.Value || _benchmark.IsCapturing;
                    SetProfilerMetricsActive(true);
                    ExportDiagnosticReport();
                    if (!keepMetrics) SetProfilerMetricsActive(false);
                }
                if (_configuration.ShadowToggleKey.Value.IsDown()) _configuration.ShadowEnabled.Value = !_configuration.ShadowEnabled.Value;
                if (_configuration.SkinningToggleKey.Value.IsDown()) _configuration.SkinningEnabled.Value = !_configuration.SkinningEnabled.Value;
                if (_configuration.PipReplacementToggleKey.Value.IsDown())
                {
                    _configuration.PipReplacementEnabled.Value = !_configuration.PipReplacementEnabled.Value;
                    Logger.LogWarning("Scope mode: " + (_configuration.PipReplacementEnabled.Value
                        ? "main-camera zoom (PiP disabled)" : "full-resolution vanilla PiP") + ".");
                }
                SyncOptimizationFeatures(_configuration.OptimizationsEnabled.Value);
                _pipScopes.Tick(_configuration.OptimizationsEnabled.Value);
                _headlessAuthority.Tick(now);
                _framePacing.Tick(now);
                _cpuThreading.Tick(now);
                _compatibility.Tick(now);
                _dynamicMaps.Tick(now);
                if (_lifecycle.State != RaidState.Started)
                {
                    SetProfilerMetricsActive(false);
                    return;
                }

                if (_configuration.CaptureKey.Value.IsDown())
                    _configuration.ContinuousCaptureEnabled.Value = !_configuration.ContinuousCaptureEnabled.Value;
                SetProfilerMetricsActive(_overlay.Visible || _configuration.ContinuousCaptureEnabled.Value || _benchmark.IsCapturing);

                _botCounter.Tick(now);

                _timing.Initialize(_configuration.MethodTimingEnabled.Value);
                _timing.SetRuntimeEnabled(_configuration.MethodTimingEnabled.Value && (!_configuration.MethodTimingCaptureOnly.Value || _benchmark.IsCapturing));

                double frameMs = Time.unscaledDeltaTime * 1000.0;
                _entities.Tick(now, _configuration.RemoteUpdateBudgetInterval.Value);
                _fika.Tick(now);
                _shadows.ObserveFrame(frameMs / 1000.0, _metrics.PreferredTimeMs("CPU Main Thread Frame Time", "PlayerLoop") ?? frameMs);
                _shadows.Tick(now);
                _skinning.Tick(now);
                _declutter.Tick();
                _remoteBudget.Tick(now);
                _combatPresentation.Tick(now);
                if (!_configuration.ContinuousCaptureEnabled.Value && _benchmark.IsCapturing)
                {
                    _benchmark.Finish(false);
                    ExportDiagnosticReport();
                }
                else if (_configuration.ContinuousCaptureEnabled.Value && !_benchmark.IsCapturing)
                {
                    double duration = _configuration.Validated.CaptureSeconds;
                    _timing.ResetAggregates(now);
                    _timing.SetRuntimeEnabled(_configuration.MethodTimingEnabled.Value);
                    _processThreads.Reset(now);
                    _metrics.BeginCapture();
                    _benchmark.Start(now, duration, ReadMapName(_lifecycle.World), FeatureState(), Path.Combine(_outputRoot, "benchmarks"), _configuration.ExportCsv.Value, _configuration.ExportJson.Value);
                }

                if (_benchmark.IsCapturing)
                {
                    _processThreads.Tick(now);
                    _metrics.SampleCapture();
                }

                if (_overlay.Visible || _benchmark.IsCapturing)
                {
                    _counts = _entities.CountNow(now);
                    if (_overlay.Visible) _overlay.AddFrame(frameMs);
                }
                bool captureCompleted = _benchmark.IsCapturing && _benchmark.Record(now, frameMs, _metrics, _counts, _shadows.Counters,
                    _skinning.Counters, default, _declutter.Counters, _areaLights.Counters, _remoteBudget.Counters, _combatPresentation.Counters, _pipScopes.Counters,
                    _configuration.OptimizationsEnabled.Value, _compatibility.FastLookups, _fika.ServerFps);
                if (captureCompleted) ExportDiagnosticReport();
                _overlay.SuiteAverageMs = _suiteAverageMs;
                if (_overlay.NeedsRefresh(now))
                {
                    string methodText = _timing.GetOverlayText(now);
                    _overlay.DisplayMode = _configuration.OverlayDisplayMode.Value;
                    _overlay.PresetName = _configuration.Preset.Value.ToString();
                    _overlay.OptimizationsActive = _configuration.OptimizationsEnabled.Value;
                    _overlay.OptimizationSummary = "Remote CPU: " + RemoteBudgetStatus() + "\nArea lights: " + _areaLights.StatusText
                        + "\nWorld presentation: " + _worldPresentation.StatusText
                        + "\nCPU threads: " + _cpuThreading.StatusText + "\nPiP scope: " + PipScopeStatus()
                        + "\nCombat presentation: " + _combatPresentation.StatusText + "\nFrame pacing: " + _framePacing.StatusText
                        + "\nDynamic Maps: " + _dynamicMaps.StatusText + "\nCompatibility: " + _compatibility.StatusText + "\nCombat logs: " + _hotLogs.StatusText;
                    _overlay.Refresh(now, _counts, _metrics, _fika, _benchmark, _lifecycle.State.ToString(), ExperimentStatus(), methodText);
                }
            }
            catch (Exception ex)
            {
                float now = Time.realtimeSinceStartup;
                if (now >= _nextPluginErrorLog)
                {
                    _nextPluginErrorLog = now + 5f;
                    _exceptions.Add("Plugin.Update", ex, this);
                    Logger.LogError("Plugin update failed open; game execution is unchanged: " + ex);
                }
            }
            finally
            {
                double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                _suiteAverageMs = _suiteAverageMs == 0 ? elapsed : (_suiteAverageMs * 0.99) + (elapsed * 0.01);
            }
        }

        private void OnGUI()
        {
            if (_configuration == null || !_configuration.Enabled.Value) return;
            _overlay.Draw();
            _botCounter?.Draw();
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnConfigurationChanged;
            try { Config.Save(); }
            catch { }
            _lifecycle.Shutdown();
            _metrics?.Dispose();
            _benchmark?.Finish(true);
            _entities.Clear();
            _fika.Clear();
            _timing?.Shutdown();
            _shadows?.Shutdown();
            _skinning?.Shutdown();
            _aggressiveQuality?.Shutdown();
            _areaLights?.Shutdown();
            _worldPresentation?.Shutdown();
            _declutter?.Shutdown();
            _remoteBudget?.Shutdown();
            _cpuThreading?.Shutdown();
            _framePacing?.Shutdown();
            _compatibility?.Shutdown();
            _dynamicMaps?.Shutdown();
            _hotLogs?.Shutdown();
            _combatPresentation?.Shutdown();
            _pipScopes?.Shutdown();
            _headlessAuthority?.Shutdown();
            Logger.LogInfo(PluginName + " shut down and released diagnostic resources.");
        }

        private void OnRaidStarted(GameWorld world)
        {
            _entities.Start(world);
            _metrics.Dispose();
            _profilerMetricsActive = false;
            _availableProfilerMetricsWrittenForRaid = false;
            _fika.Clear();
            _timing.Initialize(_configuration.MethodTimingEnabled.Value);
            _shadows.OnRaidStarted();
            _skinning.OnRaidStarted();
            _aggressiveQuality.OnRaidStarted();
            _areaLights.OnRaidStarted();
            _worldPresentation.OnRaidStarted();
            _declutter.OnRaidStarted();
            _remoteBudget.OnRaidStarted();
            _cpuThreading.OnRaidStarted();
            _framePacing.OnRaidStarted();
            _compatibility.SetWorld(world);
            _compatibility.OnRaidStarted();
            _dynamicMaps.OnRaidStarted();
            _botCounter.OnRaidStarted();
            _hotLogs.OnRaidStarted();
            _combatPresentation.OnRaidStarted();
            _pipScopes.OnRaidStarted();
            _headlessAuthority.OnRaidStarted();
            _overlay.Reset();
            _overlay.Visible = _configuration.OverlayEnabled.Value;
            Logger.LogInfo("Raid started: optimizations active; diagnostic recorders remain off until the overlay or capture is requested.");
            _runtime.Log(Logger);
        }

        private void OnRaidEnded(GameWorld world)
        {
            _shadows.OnRaidEnded();
            _skinning.OnRaidEnded();
            _aggressiveQuality.OnRaidEnded();
            _areaLights.OnRaidEnded();
            _worldPresentation.OnRaidEnded();
            _declutter.OnRaidEnded();
            _remoteBudget.OnRaidEnded();
            _cpuThreading.OnRaidEnded();
            _framePacing.OnRaidEnded();
            _compatibility.OnRaidEnded();
            _dynamicMaps.OnRaidEnded();
            _botCounter.OnRaidEnded();
            _hotLogs.OnRaidEnded();
            _combatPresentation.OnRaidEnded();
            _pipScopes.OnRaidEnded();
            _headlessAuthority.OnRaidEnded();
            _benchmark.Finish(true);
            _metrics.Dispose();
            _profilerMetricsActive = false;
            _entities.Clear();
            _fika.Clear();
            _overlay.Reset();
            Logger.LogInfo("Raid ended: capture closed and diagnostic resources disposed.");
        }

        private void SetProfilerMetricsActive(bool active)
        {
            if (_metrics == null || _profilerMetricsActive == active) return;
            _profilerMetricsActive = active;
            if (!active)
            {
                _metrics.Dispose();
                return;
            }

            _metrics.Start();
            if (_availableProfilerMetricsWrittenForRaid) return;
            _availableProfilerMetricsWrittenForRaid = true;
            try
            {
                string report = Path.Combine(_outputRoot, "AVAILABLE_PROFILER_METRICS.runtime.md");
                _metrics.WriteAvailableReport(report);
            }
            catch (Exception ex) { _exceptions.Add("Profiler metric report", ex); }
        }

        private void ExportDiagnosticReport()
        {
            try
            {
                string benchmarkConfiguration = "durationSeconds=" + _configuration.Validated.CaptureSeconds + ";exportCsv=" + _configuration.ExportCsv.Value
                    + ";exportJson=" + _configuration.ExportJson.Value + ";continuous=" + _configuration.ContinuousCaptureEnabled.Value;
                double profiledSeconds = Math.Max(0.001, _benchmark.ElapsedSeconds);
                string processThreadReport = _metrics.BuildCumulativeReport() + Environment.NewLine + _processThreads.BuildReport();
                string methodSnapshot = _timing.GetDiagnosticSnapshot(profiledSeconds);
                string profilePath = _timing.WriteCumulativeReport(Path.Combine(_outputRoot, "profiles"), ReadMapName(_lifecycle.World), profiledSeconds, processThreadReport);
                string path = DiagnosticReport.Write(Path.Combine(_outputRoot, "diagnostics"), _runtime, _metrics, _counts, _exceptions, FeatureState() + ";" + ExperimentStatus(), _combatPresentation.StatusText, benchmarkConfiguration, _timing.PatchReport, methodSnapshot, _suiteAverageMs);
                Logger.LogInfo("Diagnostic report exported: " + path);
                Logger.LogInfo("Cumulative CPU profile exported: " + profilePath);
            }
            catch (Exception ex)
            {
                _exceptions.Add("Diagnostic report", ex);
                Logger.LogError("Diagnostic report export failed: " + ex);
            }
        }

        private string FeatureState()
        {
            return "RemoteCharacterShadowLOD=" + (_shadows != null && _shadows.IsEnabled ? "enabled" : "disabled")
                + ";OffscreenSkinning=" + (_skinning != null && _skinning.IsEnabled ? "enabled" : "disabled")
                + ";AggressiveQuality=" + (_aggressiveQuality != null && _aggressiveQuality.IsEnabled ? "enabled" : "disabled")
                + ";AreaLightCache=" + (_areaLights != null && _areaLights.IsEnabled ? "enabled" : "disabled")
                + ";WorldPresentationBudget=" + (_worldPresentation != null && _worldPresentation.IsEnabled ? "enabled" : "disabled")
                + ";CosmeticDeclutter=" + (_declutter != null && _declutter.IsEnabled ? "enabled" : "disabled")
                + ";RemoteCpuBudget=" + (_remoteBudget != null && _remoteBudget.IsEnabled ? "enabled" : "disabled")
                + ";AllLogicalProcessors=" + (_cpuThreading != null && _cpuThreading.IsEnabled ? "enabled" : "disabled")
                + ";FramePacing=" + (_framePacing != null && _framePacing.IsEnabled ? "enabled" : "disabled")
                + ";KnownModFixes=" + (_compatibility != null && _compatibility.IsEnabled ? "enabled" : "disabled")
                + ";DynamicMapsBudget=" + (_dynamicMaps != null && _dynamicMaps.IsEnabled ? "enabled" : "disabled")
                + ";CombatLogSuppression=" + (_hotLogs != null && _hotLogs.IsEnabled ? "enabled" : "disabled")
                + ";CombatPresentation=" + (_combatPresentation != null && _combatPresentation.IsEnabled ? "enabled" : "disabled")
                + ";PipScopeBudget=" + (_pipScopes != null && _pipScopes.IsEnabled ? "enabled" : "disabled")
                + ";HeadlessAuthority=" + (_headlessAuthority != null && _headlessAuthority.IsEnabled ? "enabled" : "disabled")
                + ";AllOptimizations=" + (_configuration.OptimizationsEnabled.Value ? "ON" : "OFF")
                + ";ContinuousCapture=" + _configuration.ContinuousCaptureEnabled.Value
                + ";Preset=" + _configuration.Preset.Value;
        }

        private string ShadowStatus()
        {
            ShadowFeatureCounters counters = _shadows.Counters;
            return _shadows.StatusText + " | effective " + counters.EffectiveDistance.ToString("F0") + " m | AI " + counters.RegisteredAi + " beyond " + counters.BeyondThreshold + " renderers " + counters.TrackedRenderers + " disabled " + counters.DisabledRenderers + " cost " + counters.AverageMs.ToString("F3") + " ms";
        }

        private string SkinningStatus()
        {
            SkinningFeatureCounters counters = _skinning.Counters;
            return _skinning.StatusText + " | AI " + counters.RegisteredAi + " offscreen " + counters.OffscreenAi + " candidates " + counters.CandidateRenderers + " changed " + counters.ModifiedRenderers + " cost " + counters.AverageMs.ToString("F3") + " ms";
        }

        private string DeclutterStatus()
        {
            DeclutterCounters counters = _declutter.Counters;
            return _declutter.StatusText + " | scanned " + counters.Scanned + " candidates " + counters.Candidates + " hidden " + counters.Hidden
                + " | " + (counters.Complete ? "complete" : "scanning") + " discovery " + counters.DiscoveryMs.ToString("F1") + " ms batch " + counters.AverageBatchMs.ToString("F3") + " ms";
        }

        private string RemoteBudgetStatus()
        {
            RemoteUpdateBudgetCounters counters = _remoteBudget.Counters;
            return _remoteBudget.StatusText + " | remote " + counters.RemoteCharacters + " hidden " + counters.HiddenCharacters + " budgeted " + counters.BudgetedCharacters
                + " baked-hidden " + counters.BakedHiddenCharacters
                + " visible>distance " + counters.VisibleDistantCharacters + " frozen-hidden " + counters.FrozenHiddenCharacters
                + " animators " + counters.CulledAnimators + " skipped props " + counters.SkippedPropUpdates + " triggers " + counters.SkippedTriggerSearches
                + " presentation " + counters.SkippedPresentationUpdates
                + " | cost " + counters.AverageMs.ToString("F3") + " ms";
        }

        private string PipScopeStatus()
        {
            return _pipScopes.StatusText;
        }

        private string ExperimentStatus() => "Shadows: " + ShadowStatus()
            + "\nOffscreen skinning: " + SkinningStatus()
            + "\nAggressive quality: " + _aggressiveQuality.StatusText
            + "\nArea-light command cache: " + _areaLights.StatusText
            + "\nWorld presentation: " + _worldPresentation.StatusText
            + "\nDeclutter: " + DeclutterStatus()
            + "\nRemote CPU budget: " + RemoteBudgetStatus()
            + "\nCPU threads: " + _cpuThreading.StatusText
            + "\nPiP scope camera: " + PipScopeStatus()
            + "\nHeadless authority: " + _headlessAuthority.StatusText
            + "\nCombat presentation: " + _combatPresentation.StatusText
            + "\nFrame pacing: " + _framePacing.StatusText
            + "\nCompatibility: " + _compatibility.StatusText
            + "\nDynamic Maps: " + _dynamicMaps.StatusText
            + "\nBot counter: " + _botCounter.StatusText
            + "\nCombat logs: " + _hotLogs.StatusText;

        private void ApplyPresetIfChanged()
        {
            if (_configuration == null || _configuration.Preset.Value == _lastPreset) return;
            _lastPreset = _configuration.Preset.Value;
            if (_lastPreset == PerformancePreset.Custom) return;

            _applyingPreset = true;
            bool previousSaveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            try
            {

            _configuration.KnownModFixesEnabled.Value = true;
            _configuration.DynamicMapsOptimizationEnabled.Value = true;
            _configuration.ExportJson.Value = false;
            _configuration.FramePacingEnabled.Value = true;
            _configuration.RemoteUpdateBudgetEnabled.Value = true;
            _configuration.RemoteAnimatorCullingEnabled.Value = true;
            _configuration.RemotePresentationBudgetEnabled.Value = true;
            _configuration.UseAllLogicalProcessors.Value = true;
            _configuration.HotPathLogSuppressionEnabled.Value = true;
            _configuration.CombatPresentationEnabled.Value = true;
            _configuration.SoundOnlyRemoteShots.Value = true;
            _configuration.CullDistantMuzzleEffects.Value = true;
            _configuration.CullDistantImpactEffects.Value = true;
            _configuration.CullHiddenRemoteLights.Value = true;
            _configuration.RequireBakedOcclusionForSoundOnly.Value = true;
            _configuration.AggressiveModeEnabled.Value = true;
            _configuration.AreaLightCacheEnabled.Value = true;
            _configuration.WorldPresentationBudgetEnabled.Value = true;
            _configuration.SkinningEnabled.Value = true;
            _configuration.ShadowEnabled.Value = true;
            _configuration.CosmeticDeclutterEnabled.Value = true;
            _configuration.PipScopeOptimizationEnabled.Value = true;
            _configuration.HeadlessAuthorityEnabled.Value = true;
            _configuration.RemoteFreezeHiddenPresentation.Value = true;
            _configuration.OptimizationsEnabled.Value = true;

            if (_lastPreset == PerformancePreset.Balanced)
            {
                _configuration.AggressiveTextureMipLimit.Value = 0;
                _configuration.AggressiveShadowDistance.Value = 75f;
                _configuration.AggressiveShadowResolution.Value = ShadowResolution.Medium;
                _configuration.AggressiveShadowCascades.Value = 2;
                _configuration.AggressivePixelLights.Value = 2;
                _configuration.AggressiveParticleRaycastBudget.Value = 64;
                _configuration.AggressiveAmbientReflectionRate.Value = 20f;
                _configuration.AggressiveAmbientCommandRate.Value = 30f;
                _configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDistance.Value = 60f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.3f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 2;
                _configuration.RemoteAggressivePresentationDistance.Value = 60f;
                _configuration.RemoteVisiblePresentationDivisor.Value = 2;
                _configuration.ShadowDistance.Value = 100f;
                _configuration.ShadowMinimumDistance.Value = 60f;
                _configuration.PipScopeResolutionScale.Value = 1f;
                _configuration.AreaLightRefreshFrames.Value = 2;
                _configuration.CullingRefreshRate.Value = 60f;
                _configuration.DistantShadowRefreshRate.Value = 30f;
                _configuration.DeferredDecalRefreshRate.Value = 30f;
                _configuration.WeatherRefreshRate.Value = 20f;
                _configuration.SoundOnlyShotDistance.Value = 180f;
                _configuration.IncomingShotSafetyRadius.Value = 45f;
                _configuration.RemoteCombatRecentVisibilityHold.Value = 1f;
                _configuration.DistantMuzzleEffectDistance.Value = 75f;
                _configuration.DistantImpactEffectDistance.Value = 120f;
                _configuration.HiddenRemoteLightDistance.Value = 90f;
            }
            else if (_lastPreset == PerformancePreset.Performance)
            {
                _configuration.AggressiveTextureMipLimit.Value = 0;
                _configuration.AggressiveShadowDistance.Value = 45f;
                _configuration.AggressiveShadowResolution.Value = ShadowResolution.Low;
                _configuration.AggressiveShadowCascades.Value = 2;
                _configuration.AggressivePixelLights.Value = 1;
                _configuration.AggressiveParticleRaycastBudget.Value = 16;
                _configuration.AggressiveAmbientReflectionRate.Value = 10f;
                _configuration.AggressiveAmbientCommandRate.Value = 15f;
                _configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDistance.Value = 25f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 8;
                _configuration.RemoteAggressivePresentationDistance.Value = 50f;
                _configuration.RemoteVisiblePresentationDivisor.Value = 2;
                _configuration.ShadowDistance.Value = 50f;
                _configuration.ShadowMinimumDistance.Value = 25f;
                _configuration.ShadowTargetFps.Value = 45f;
                _configuration.PipScopeResolutionScale.Value = 1f;
                _configuration.AreaLightRefreshFrames.Value = 4;
                _configuration.CullingRefreshRate.Value = 30f;
                _configuration.DistantShadowRefreshRate.Value = 15f;
                _configuration.DeferredDecalRefreshRate.Value = 15f;
                _configuration.WeatherRefreshRate.Value = 10f;
                _configuration.SoundOnlyShotDistance.Value = 120f;
                _configuration.IncomingShotSafetyRadius.Value = 35f;
                _configuration.RemoteCombatRecentVisibilityHold.Value = 0.75f;
                _configuration.DistantMuzzleEffectDistance.Value = 60f;
                _configuration.DistantImpactEffectDistance.Value = 90f;
                _configuration.HiddenRemoteLightDistance.Value = 70f;
            }
            else if (_lastPreset == PerformancePreset.Extreme)
            {
                // Preserve texture sharpness. Recover CPU/render/VRAM headroom from transient and
                // distance-scaled presentation instead of globally discarding texture mips.
                _configuration.AggressiveTextureMipLimit.Value = 0;
                _configuration.AggressiveShadowDistance.Value = 28f;
                _configuration.AggressiveShadowResolution.Value = ShadowResolution.Low;
                _configuration.AggressiveShadowCascades.Value = 2;
                _configuration.AggressivePixelLights.Value = 0;
                _configuration.AggressiveParticleRaycastBudget.Value = 8;
                _configuration.AggressiveAmbientReflectionRate.Value = 10f;
                _configuration.AggressiveAmbientCommandRate.Value = 12f;
                _configuration.RemoteUpdateBudgetInterval.Value = 0.075f;
                _configuration.RemoteUpdateBudgetDistance.Value = 20f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.05f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 8;
                _configuration.RemoteAggressivePresentationDistance.Value = 50f;
                _configuration.RemoteVisiblePresentationDivisor.Value = 2;
                _configuration.ShadowDistance.Value = 35f;
                _configuration.ShadowMinimumDistance.Value = 20f;
                _configuration.ShadowTargetFps.Value = 60f;
                _configuration.PipScopeResolutionScale.Value = 1f;
                _configuration.AreaLightRefreshFrames.Value = 8;
                _configuration.CullingRefreshRate.Value = 30f;
                _configuration.DistantShadowRefreshRate.Value = 15f;
                _configuration.DeferredDecalRefreshRate.Value = 15f;
                _configuration.WeatherRefreshRate.Value = 10f;
                _configuration.SoundOnlyShotDistance.Value = 90f;
                _configuration.IncomingShotSafetyRadius.Value = 40f;
                _configuration.RemoteCombatRecentVisibilityHold.Value = 0.35f;
                _configuration.DistantMuzzleEffectDistance.Value = 40f;
                _configuration.DistantImpactEffectDistance.Value = 60f;
                _configuration.HiddenRemoteLightDistance.Value = 45f;
                _configuration.DistantShellPhysicsDistance.Value = 18f;
                _configuration.BulletFlybyAudioRate.Value = 20;
            }
            }
            finally
            {
                Config.SaveOnConfigSet = previousSaveOnSet;
                _applyingPreset = false;
            }
            _refreshAggressiveQuality = true;
            _configurationChanged = true;
            try { Config.Save(); }
            catch (Exception ex) { Logger?.LogWarning("Could not save the selected performance preset: " + ex.Message); }
            Logger?.LogInfo("Applied performance preset: " + _lastPreset + ". All optimization features are enabled; diagnostics remain independently controlled.");
        }

        private void OnConfigurationChanged(object sender, SettingChangedEventArgs args)
        {
            _configurationChanged = true;
            if (args?.ChangedSetting?.Definition.Section == "Aggressive") _refreshAggressiveQuality = true;
            if (!_applyingPreset && _configuration != null && args?.ChangedSetting != _configuration.Preset
                && args?.ChangedSetting?.Definition.Section != "Diagnostics"
                && args?.ChangedSetting?.Definition.Section != "HUD - Bot Counter"
                && args?.ChangedSetting?.Definition.Section != "General"
                && args?.ChangedSetting?.Definition.Section != "Quick Setup")
            {
                _configuration.Preset.Value = PerformancePreset.Custom;
            }
        }

        private void ApplyDynamicConfiguration()
        {
            if (!_configurationChanged || _configuration == null) return;
            _configurationChanged = false;
            if (_overlay != null) _overlay.Visible = _configuration.OverlayEnabled.Value;
            if (_refreshAggressiveQuality)
            {
                _refreshAggressiveQuality = false;
                _aggressiveQuality?.Refresh();
            }
        }

        private void SyncOptimizationFeatures(bool masterEnabled)
        {
            SyncFeature(_configuration.ShadowEnabled, _shadows.IsEnabled, _shadows.IsAvailable, masterEnabled, _shadows.SetEnabled);
            SyncFeature(_configuration.SkinningEnabled, _skinning.IsEnabled, _skinning.IsAvailable, masterEnabled, _skinning.SetEnabled);
            SyncFeature(_configuration.AggressiveModeEnabled, _aggressiveQuality.IsEnabled, _aggressiveQuality.IsAvailable, masterEnabled, _aggressiveQuality.SetEnabled);
            SyncFeature(_configuration.AreaLightCacheEnabled, _areaLights.IsEnabled, _areaLights.IsAvailable, masterEnabled, _areaLights.SetEnabled);
            SyncFeature(_configuration.WorldPresentationBudgetEnabled, _worldPresentation.IsEnabled, _worldPresentation.IsAvailable, masterEnabled, _worldPresentation.SetEnabled);
            SyncFeature(_configuration.CosmeticDeclutterEnabled, _declutter.IsEnabled, _declutter.IsAvailable, masterEnabled, _declutter.SetEnabled);
            SyncFeature(_configuration.RemoteUpdateBudgetEnabled, _remoteBudget.IsEnabled, _remoteBudget.IsAvailable, masterEnabled, _remoteBudget.SetEnabled);
            SyncFeature(_configuration.UseAllLogicalProcessors, _cpuThreading.IsEnabled, _cpuThreading.IsAvailable, masterEnabled, _cpuThreading.SetEnabled);
            SyncFeature(_configuration.FramePacingEnabled, _framePacing.IsEnabled, _framePacing.IsAvailable, masterEnabled, _framePacing.SetEnabled);
            SyncFeature(_configuration.KnownModFixesEnabled, _compatibility.IsEnabled, _compatibility.IsAvailable, masterEnabled, _compatibility.SetEnabled);
            SyncFeature(_configuration.DynamicMapsOptimizationEnabled, _dynamicMaps.IsEnabled, _dynamicMaps.IsAvailable, masterEnabled, _dynamicMaps.SetEnabled);
            SyncFeature(_configuration.HotPathLogSuppressionEnabled, _hotLogs.IsEnabled, _hotLogs.IsAvailable, masterEnabled, _hotLogs.SetEnabled);
            SyncFeature(_configuration.CombatPresentationEnabled, _combatPresentation.IsEnabled, _combatPresentation.IsAvailable, masterEnabled, _combatPresentation.SetEnabled);
            SyncFeature(_configuration.PipScopeOptimizationEnabled, _pipScopes.IsEnabled, _pipScopes.IsAvailable, masterEnabled, _pipScopes.SetEnabled);
            SyncFeature(_configuration.HeadlessAuthorityEnabled, _headlessAuthority.IsEnabled, _headlessAuthority.IsAvailable, masterEnabled, _headlessAuthority.SetEnabled);
        }

        private static void SyncFeature(ConfigEntry<bool> configuredEntry, bool current, bool available, bool masterEnabled, Action<bool> setEnabled)
        {
            bool configured = configuredEntry.Value;
            bool desired = masterEnabled && configured;
            if (desired == current || (desired && !available)) return;
            setEnabled(desired);
            // Feature classes also expose standalone F12 toggles and therefore write their own
            // entry. Preserve the user's configured preference while the runtime A/B master is off.
            configuredEntry.Value = configured;
        }

        private static string ReadMapName(GameWorld world)
        {
            if (world == null) return "unknown-map";
            try
            {
                Type type = world.GetType();
                PropertyInfo property = type.GetProperty("LocationId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(string)) return (string)property.GetValue(world, null) ?? "unknown-map";
                FieldInfo field = type.GetField("LocationId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(string)) return (string)field.GetValue(world) ?? "unknown-map";
            }
            catch { }
            return "unknown-map";
        }
    }
}
