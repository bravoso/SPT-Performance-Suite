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
        public const string PluginVersion = "0.8.0";

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
        private RemoteCharacterShadowFeature _shadows;
        private RemoteAiOffscreenSkinningFeature _skinning;
        private AggressiveQualityFeature _aggressiveQuality;
        private CosmeticDeclutterFeature _declutter;
        private RemoteUpdateBudgetFeature _remoteBudget;
        private CpuThreadingFeature _cpuThreading;
        private FramePacingFeature _framePacing;
        private KnownModCompatibilityFeature _compatibility;
        private HotPathLogSuppressionFeature _hotLogs;
        private CombatPresentationBudgetFeature _combatPresentation;
        private PipScopeOptimizationFeature _pipScopes;
        private EntityCounts _counts;
        private double _suiteAverageMs;
        private string _outputRoot;
        private float _nextPluginErrorLog;
        private PerformancePreset _lastPreset = (PerformancePreset)(-1);

        private void Awake()
        {
            _configuration = new PluginConfiguration(Config);
            ApplyPresetIfChanged();
            _runtime = RuntimeInformation.Detect();
            _metrics = new ProfilerMetrics(Logger);
            _overlay = new DiagnosticsOverlay { Visible = _configuration.OverlayEnabled.Value };
            _benchmark = new BenchmarkRecorder(Logger);
            _timing = new MethodTimingFramework(Logger);
            _shadows = new RemoteCharacterShadowFeature(Logger, _configuration, _entities, _exceptions);
            _skinning = new RemoteAiOffscreenSkinningFeature(Logger, _configuration, _entities, _exceptions);
            _aggressiveQuality = new AggressiveQualityFeature(Logger, _configuration, _exceptions);
            _declutter = new CosmeticDeclutterFeature(Logger, _configuration, _exceptions);
            _remoteBudget = new RemoteUpdateBudgetFeature(Logger, _configuration, _entities, _exceptions);
            _cpuThreading = new CpuThreadingFeature(Logger, _configuration, _exceptions);
            _framePacing = new FramePacingFeature(Logger, _configuration, _exceptions);
            _compatibility = new KnownModCompatibilityFeature(Logger, _configuration, _exceptions);
            _hotLogs = new HotPathLogSuppressionFeature(Logger, _configuration, _exceptions);
            _combatPresentation = new CombatPresentationBudgetFeature(Logger, _configuration, _exceptions);
            _pipScopes = new PipScopeOptimizationFeature(Logger, _configuration, _exceptions);
            _outputRoot = Path.Combine(BepInEx.Paths.PluginPath, "TarkovPerformanceSuite");
            _lifecycle.RaidStarted += OnRaidStarted;
            _lifecycle.RaidEnded += OnRaidEnded;
            _lifecycle.StateChanged += state => { if (_configuration.VerboseLogging.Value) Logger.LogInfo("Lifecycle state: " + state); };
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Aggressive old-CPU optimizations are enabled by default; Num4 toggles the complete optimization stack for A/B testing.");
            _runtime.Log(Logger);
            _shadows.Initialize();
            _skinning.Initialize();
            _aggressiveQuality.Initialize();
            _declutter.Initialize();
            _remoteBudget.Initialize();
            _cpuThreading.Initialize();
            _framePacing.Initialize();
            _compatibility.Initialize();
            _hotLogs.Initialize();
            _combatPresentation.Initialize();
            _pipScopes.Initialize();
        }

        private void Update()
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                _benchmark.DrainCompletionLog();
                _timing.FrameBoundary(Time.realtimeSinceStartup);
                ApplyPresetIfChanged();
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

                if (_configuration.DiagnosticReportKey.Value.IsDown()) ExportDiagnosticReport();
                if (_configuration.ShadowToggleKey.Value.IsDown()) _configuration.ShadowEnabled.Value = !_configuration.ShadowEnabled.Value;
                if (_configuration.SkinningToggleKey.Value.IsDown()) _configuration.SkinningEnabled.Value = !_configuration.SkinningEnabled.Value;
                SyncOptimizationFeatures(_configuration.OptimizationsEnabled.Value);
                _framePacing.Tick(now);
                _cpuThreading.Tick(now);
                _compatibility.Tick(now);
                if (_lifecycle.State != RaidState.Started) return;

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
                if (_configuration.CaptureKey.Value.IsDown())
                    _configuration.ContinuousCaptureEnabled.Value = !_configuration.ContinuousCaptureEnabled.Value;

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
                    _benchmark.Start(now, duration, ReadMapName(_lifecycle.World), FeatureState(), Path.Combine(_outputRoot, "benchmarks"), _configuration.ExportCsv.Value);
                }

                if (_overlay.Visible || _benchmark.IsCapturing)
                {
                    _counts = _entities.CountNow(now);
                    if (_overlay.Visible) _overlay.AddFrame(frameMs);
                }
                bool captureCompleted = _benchmark.IsCapturing && _benchmark.Record(now, frameMs, _metrics, _counts, _shadows.Counters,
                    _skinning.Counters, default, _declutter.Counters, _remoteBudget.Counters, _pipScopes.Counters,
                    _configuration.OptimizationsEnabled.Value, _compatibility.FastLookups, _fika.ServerFps);
                if (captureCompleted) ExportDiagnosticReport();
                _overlay.SuiteAverageMs = _suiteAverageMs;
                if (_overlay.NeedsRefresh(now))
                {
                    string methodText = _timing.GetOverlayText(now);
                    _overlay.DisplayMode = _configuration.OverlayDisplayMode.Value;
                    _overlay.PresetName = _configuration.Preset.Value.ToString();
                    _overlay.OptimizationsActive = _configuration.OptimizationsEnabled.Value;
                    _overlay.OptimizationSummary = "Remote CPU: " + RemoteBudgetStatus() + "\nCPU threads: " + _cpuThreading.StatusText + "\nPiP scope: " + PipScopeStatus()
                        + "\nCombat presentation: " + _combatPresentation.StatusText + "\nFrame pacing: " + _framePacing.StatusText + "\nCompatibility: " + _compatibility.StatusText + "\nCombat logs: " + _hotLogs.StatusText;
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
            if (_configuration != null && _configuration.Enabled.Value) _overlay.Draw();
        }

        private void OnDestroy()
        {
            _lifecycle.Shutdown();
            _metrics?.Dispose();
            _benchmark?.Finish(true);
            _entities.Clear();
            _fika.Clear();
            _timing?.Shutdown();
            _shadows?.Shutdown();
            _skinning?.Shutdown();
            _aggressiveQuality?.Shutdown();
            _declutter?.Shutdown();
            _remoteBudget?.Shutdown();
            _cpuThreading?.Shutdown();
            _framePacing?.Shutdown();
            _compatibility?.Shutdown();
            _hotLogs?.Shutdown();
            _combatPresentation?.Shutdown();
            _pipScopes?.Shutdown();
            Logger.LogInfo(PluginName + " shut down and released diagnostic resources.");
        }

        private void OnRaidStarted(GameWorld world)
        {
            _entities.Start(world);
            _metrics.Start();
            _fika.Clear();
            _timing.Initialize(_configuration.MethodTimingEnabled.Value);
            _shadows.OnRaidStarted();
            _skinning.OnRaidStarted();
            _aggressiveQuality.OnRaidStarted();
            _declutter.OnRaidStarted();
            _remoteBudget.OnRaidStarted();
            _cpuThreading.OnRaidStarted();
            _framePacing.OnRaidStarted();
            _compatibility.SetWorld(world);
            _compatibility.OnRaidStarted();
            _hotLogs.OnRaidStarted();
            _combatPresentation.OnRaidStarted();
            _pipScopes.OnRaidStarted();
            _overlay.Reset();
            _overlay.Visible = _configuration.OverlayEnabled.Value;
            try
            {
                string report = Path.Combine(_outputRoot, "AVAILABLE_PROFILER_METRICS.runtime.md");
                _metrics.WriteAvailableReport(report);
            }
            catch (Exception ex) { _exceptions.Add("Profiler metric report", ex); }
            Logger.LogInfo("Raid started: diagnostics active; registries will populate gradually.");
            _runtime.Log(Logger);
        }

        private void OnRaidEnded(GameWorld world)
        {
            _shadows.OnRaidEnded();
            _skinning.OnRaidEnded();
            _aggressiveQuality.OnRaidEnded();
            _declutter.OnRaidEnded();
            _remoteBudget.OnRaidEnded();
            _cpuThreading.OnRaidEnded();
            _framePacing.OnRaidEnded();
            _compatibility.OnRaidEnded();
            _hotLogs.OnRaidEnded();
            _combatPresentation.OnRaidEnded();
            _pipScopes.OnRaidEnded();
            _benchmark.Finish(true);
            _metrics.Dispose();
            _entities.Clear();
            _fika.Clear();
            _overlay.Reset();
            Logger.LogInfo("Raid ended: capture closed and diagnostic resources disposed.");
        }

        private void ExportDiagnosticReport()
        {
            try
            {
                string benchmarkConfiguration = "durationSeconds=" + _configuration.Validated.CaptureSeconds + ";exportCsv=" + _configuration.ExportCsv.Value + ";continuous=" + _configuration.ContinuousCaptureEnabled.Value;
                string methodSnapshot = _timing.GetDiagnosticSnapshot(Time.realtimeSinceStartup);
                string path = DiagnosticReport.Write(Path.Combine(_outputRoot, "diagnostics"), _runtime, _metrics, _counts, _exceptions, FeatureState() + ";" + ExperimentStatus(), benchmarkConfiguration, _timing.PatchReport, methodSnapshot, _suiteAverageMs);
                Logger.LogInfo("Diagnostic report exported: " + path);
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
                + ";CosmeticDeclutter=" + (_declutter != null && _declutter.IsEnabled ? "enabled" : "disabled")
                + ";RemoteCpuBudget=" + (_remoteBudget != null && _remoteBudget.IsEnabled ? "enabled" : "disabled")
                + ";AllLogicalProcessors=" + (_cpuThreading != null && _cpuThreading.IsEnabled ? "enabled" : "disabled")
                + ";FramePacing=" + (_framePacing != null && _framePacing.IsEnabled ? "enabled" : "disabled")
                + ";KnownModFixes=" + (_compatibility != null && _compatibility.IsEnabled ? "enabled" : "disabled")
                + ";CombatLogSuppression=" + (_hotLogs != null && _hotLogs.IsEnabled ? "enabled" : "disabled")
                + ";CombatPresentation=" + (_combatPresentation != null && _combatPresentation.IsEnabled ? "enabled" : "disabled")
                + ";PipScopeBudget=" + (_pipScopes != null && _pipScopes.IsEnabled ? "enabled" : "disabled")
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
            + "\nDeclutter: " + DeclutterStatus()
            + "\nRemote CPU budget: " + RemoteBudgetStatus()
            + "\nCPU threads: " + _cpuThreading.StatusText
            + "\nPiP scope camera: " + PipScopeStatus()
            + "\nCombat presentation: " + _combatPresentation.StatusText
            + "\nFrame pacing: " + _framePacing.StatusText
            + "\nCompatibility: " + _compatibility.StatusText
            + "\nCombat logs: " + _hotLogs.StatusText;

        private void ApplyPresetIfChanged()
        {
            if (_configuration == null || _configuration.Preset.Value == _lastPreset) return;
            _lastPreset = _configuration.Preset.Value;
            if (_lastPreset == PerformancePreset.Custom) return;

            _configuration.KnownModFixesEnabled.Value = true;
            _configuration.FramePacingEnabled.Value = true;
            _configuration.RemoteUpdateBudgetEnabled.Value = true;
            _configuration.RemoteAnimatorCullingEnabled.Value = true;
            _configuration.RemotePresentationBudgetEnabled.Value = true;
            _configuration.UseAllLogicalProcessors.Value = true;
            _configuration.HotPathLogSuppressionEnabled.Value = true;
            _configuration.CombatPresentationEnabled.Value = true;
            _configuration.AggressiveModeEnabled.Value = true;
            _configuration.SkinningEnabled.Value = true;
            _configuration.ShadowEnabled.Value = true;
            _configuration.CosmeticDeclutterEnabled.Value = true;
            _configuration.PipScopeOptimizationEnabled.Value = true;
            _configuration.OptimizationsEnabled.Value = true;

            if (_lastPreset == PerformancePreset.Balanced)
            {
                _configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDistance.Value = 60f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.3f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 2;
                _configuration.ShadowDistance.Value = 100f;
                _configuration.ShadowMinimumDistance.Value = 60f;
                _configuration.PipScopeResolutionScale.Value = 0.75f;
            }
            else if (_lastPreset == PerformancePreset.OldCpuAggressive)
            {
                _configuration.RemoteUpdateBudgetInterval.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDistance.Value = 25f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.1f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 8;
                _configuration.ShadowDistance.Value = 50f;
                _configuration.ShadowMinimumDistance.Value = 25f;
                _configuration.ShadowTargetFps.Value = 45f;
                _configuration.PipScopeResolutionScale.Value = 0.5f;
            }
            Logger?.LogInfo("Applied performance preset: " + _lastPreset + ". All optimization features are enabled; unsafe LOD overrides remain removed.");
        }

        private void SyncOptimizationFeatures(bool masterEnabled)
        {
            SyncFeature(_configuration.ShadowEnabled, _shadows.IsEnabled, _shadows.IsAvailable, masterEnabled, _shadows.SetEnabled);
            SyncFeature(_configuration.SkinningEnabled, _skinning.IsEnabled, _skinning.IsAvailable, masterEnabled, _skinning.SetEnabled);
            SyncFeature(_configuration.AggressiveModeEnabled, _aggressiveQuality.IsEnabled, _aggressiveQuality.IsAvailable, masterEnabled, _aggressiveQuality.SetEnabled);
            SyncFeature(_configuration.CosmeticDeclutterEnabled, _declutter.IsEnabled, _declutter.IsAvailable, masterEnabled, _declutter.SetEnabled);
            SyncFeature(_configuration.RemoteUpdateBudgetEnabled, _remoteBudget.IsEnabled, _remoteBudget.IsAvailable, masterEnabled, _remoteBudget.SetEnabled);
            SyncFeature(_configuration.UseAllLogicalProcessors, _cpuThreading.IsEnabled, _cpuThreading.IsAvailable, masterEnabled, _cpuThreading.SetEnabled);
            SyncFeature(_configuration.FramePacingEnabled, _framePacing.IsEnabled, _framePacing.IsAvailable, masterEnabled, _framePacing.SetEnabled);
            SyncFeature(_configuration.KnownModFixesEnabled, _compatibility.IsEnabled, _compatibility.IsAvailable, masterEnabled, _compatibility.SetEnabled);
            SyncFeature(_configuration.HotPathLogSuppressionEnabled, _hotLogs.IsEnabled, _hotLogs.IsAvailable, masterEnabled, _hotLogs.SetEnabled);
            SyncFeature(_configuration.CombatPresentationEnabled, _combatPresentation.IsEnabled, _combatPresentation.IsAvailable, masterEnabled, _combatPresentation.SetEnabled);
            SyncFeature(_configuration.PipScopeOptimizationEnabled, _pipScopes.IsEnabled, _pipScopes.IsAvailable, masterEnabled, _pipScopes.SetEnabled);
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
