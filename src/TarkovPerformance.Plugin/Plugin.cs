using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
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
        public const string PluginVersion = "0.4.0";

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
        private RemoteAiRenderLodFeature _renderLod;
        private CosmeticDeclutterFeature _declutter;
        private RemoteUpdateBudgetFeature _remoteBudget;
        private FramePacingFeature _framePacing;
        private KnownModCompatibilityFeature _compatibility;
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
            _renderLod = new RemoteAiRenderLodFeature(Logger, _configuration, _entities, _exceptions);
            _declutter = new CosmeticDeclutterFeature(Logger, _configuration, _exceptions);
            _remoteBudget = new RemoteUpdateBudgetFeature(Logger, _configuration, _entities, _exceptions);
            _framePacing = new FramePacingFeature(Logger, _configuration, _exceptions);
            _compatibility = new KnownModCompatibilityFeature(Logger, _configuration, _exceptions);
            _outputRoot = Path.Combine(BepInEx.Paths.PluginPath, "TarkovPerformanceSuite");
            _lifecycle.RaidStarted += OnRaidStarted;
            _lifecycle.RaidEnded += OnRaidEnded;
            _lifecycle.StateChanged += state => { if (_configuration.VerboseLogging.Value) Logger.LogInfo("Lifecycle state: " + state); };
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. All optimization experiments default to disabled.");
            _runtime.Log(Logger);
            _shadows.Initialize();
            _skinning.Initialize();
            _aggressiveQuality.Initialize();
            _renderLod.Initialize();
            _declutter.Initialize();
            _remoteBudget.Initialize();
            _framePacing.Initialize();
            _compatibility.Initialize();
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
                    _shadows.SetEnabled(false);
                    _skinning.SetEnabled(false);
                    _aggressiveQuality.SetEnabled(false);
                    _renderLod.SetEnabled(false);
                    _declutter.SetEnabled(false);
                    _remoteBudget.SetEnabled(false);
                    _framePacing.SetEnabled(false);
                    _compatibility.SetEnabled(false);
                    return;
                }
                float now = Time.realtimeSinceStartup;
                _lifecycle.Tick(now);

                if (_configuration.OverlayKey.Value.IsDown())
                {
                    _overlay.Visible = !_overlay.Visible;
                    _configuration.OverlayEnabled.Value = _overlay.Visible;
                }

                if (_configuration.DiagnosticReportKey.Value.IsDown()) ExportDiagnosticReport();
                if (_configuration.ShadowEnabled.Value != _shadows.IsEnabled) _shadows.SetEnabled(_configuration.ShadowEnabled.Value);
                if (_configuration.ShadowToggleKey.Value.IsDown()) _shadows.SetEnabled(!_shadows.IsEnabled);
                if (_configuration.SkinningEnabled.Value != _skinning.IsEnabled) _skinning.SetEnabled(_configuration.SkinningEnabled.Value);
                if (_configuration.SkinningToggleKey.Value.IsDown()) _skinning.SetEnabled(!_skinning.IsEnabled);
                if (_configuration.AggressiveModeEnabled.Value != _aggressiveQuality.IsEnabled) _aggressiveQuality.SetEnabled(_configuration.AggressiveModeEnabled.Value);
                if (_configuration.RemoteAiRenderLodEnabled.Value != _renderLod.IsEnabled) _renderLod.SetEnabled(_configuration.RemoteAiRenderLodEnabled.Value);
                if (_configuration.CosmeticDeclutterEnabled.Value != _declutter.IsEnabled) _declutter.SetEnabled(_configuration.CosmeticDeclutterEnabled.Value);
                if (_configuration.RemoteUpdateBudgetEnabled.Value != _remoteBudget.IsEnabled) _remoteBudget.SetEnabled(_configuration.RemoteUpdateBudgetEnabled.Value);
                if (_configuration.FramePacingEnabled.Value != _framePacing.IsEnabled) _framePacing.SetEnabled(_configuration.FramePacingEnabled.Value);
                if (_configuration.KnownModFixesEnabled.Value != _compatibility.IsEnabled) _compatibility.SetEnabled(_configuration.KnownModFixesEnabled.Value);
                _framePacing.Tick(now);
                _compatibility.Tick(now);
                if (_lifecycle.State != RaidState.Started) return;

                _timing.Initialize(_configuration.MethodTimingEnabled.Value);
                _timing.SetRuntimeEnabled(_configuration.MethodTimingEnabled.Value && (!_configuration.MethodTimingCaptureOnly.Value || _benchmark.IsCapturing));

                double frameMs = Time.unscaledDeltaTime * 1000.0;
                _entities.Tick(now);
                _fika.Tick(now);
                _shadows.ObserveFrame(frameMs / 1000.0, _metrics.PreferredTimeMs("CPU Main Thread Frame Time", "PlayerLoop") ?? frameMs);
                _shadows.Tick(now);
                _skinning.Tick(now);
                _renderLod.Tick(now);
                _declutter.Tick();
                _remoteBudget.Tick(now);
                bool capturePressed = _configuration.CaptureKey.Value.IsDown() && !_benchmark.IsCapturing;

                if (capturePressed)
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
                    _skinning.Counters, _renderLod.Counters, _declutter.Counters, _remoteBudget.Counters, _compatibility.FastLookups, _fika.ServerFps);
                if (captureCompleted) ExportDiagnosticReport();
                _overlay.SuiteAverageMs = _suiteAverageMs;
                if (_overlay.NeedsRefresh(now))
                {
                    string methodText = _timing.GetOverlayText(now);
                    _overlay.DisplayMode = _configuration.OverlayDisplayMode.Value;
                    _overlay.PresetName = _configuration.Preset.Value.ToString();
                    _overlay.OptimizationSummary = "Remote CPU: " + RemoteBudgetStatus() + "\nFrame pacing: " + _framePacing.StatusText + "\nCompatibility: " + _compatibility.StatusText;
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
            _renderLod?.Shutdown();
            _declutter?.Shutdown();
            _remoteBudget?.Shutdown();
            _framePacing?.Shutdown();
            _compatibility?.Shutdown();
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
            _renderLod.OnRaidStarted();
            _declutter.OnRaidStarted();
            _remoteBudget.OnRaidStarted();
            _framePacing.OnRaidStarted();
            _compatibility.SetWorld(world);
            _compatibility.OnRaidStarted();
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
            _renderLod.OnRaidEnded();
            _declutter.OnRaidEnded();
            _remoteBudget.OnRaidEnded();
            _framePacing.OnRaidEnded();
            _compatibility.OnRaidEnded();
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
                string benchmarkConfiguration = "durationSeconds=" + _configuration.Validated.CaptureSeconds + ";exportCsv=" + _configuration.ExportCsv.Value;
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
                + ";ShadowDryRun=" + _configuration.ShadowDryRun.Value
                + ";OffscreenSkinning=" + (_skinning != null && _skinning.IsEnabled ? "enabled" : "disabled")
                + ";SkinningDryRun=" + _configuration.SkinningDryRun.Value
                + ";AggressiveQuality=" + (_aggressiveQuality != null && _aggressiveQuality.IsEnabled ? "enabled" : "disabled")
                + ";RemoteAiRenderLod=" + (_renderLod != null && _renderLod.IsEnabled ? "enabled" : "disabled")
                + ";CosmeticDeclutter=" + (_declutter != null && _declutter.IsEnabled ? "enabled" : "disabled")
                + ";RemoteCpuBudget=" + (_remoteBudget != null && _remoteBudget.IsEnabled ? "enabled" : "disabled")
                + ";FramePacing=" + (_framePacing != null && _framePacing.IsEnabled ? "enabled" : "disabled")
                + ";KnownModFixes=" + (_compatibility != null && _compatibility.IsEnabled ? "enabled" : "disabled")
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

        private string RenderLodStatus()
        {
            RemoteRenderLodCounters counters = _renderLod.Counters;
            return _renderLod.StatusText + " | AI " + counters.RegisteredAi + " mid " + counters.MidTierAi + " far " + counters.FarTierAi
                + " | LODs " + counters.ForcedLodGroups + " skins " + counters.ModifiedSkinnedRenderers + " renderers " + counters.ModifiedRenderers
                + " | cost " + counters.AverageMs.ToString("F3") + " ms";
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
                + " | cost " + counters.AverageMs.ToString("F3") + " ms";
        }

        private string ExperimentStatus() => "Shadows: " + ShadowStatus()
            + "\nOffscreen skinning: " + SkinningStatus()
            + "\nAggressive quality: " + _aggressiveQuality.StatusText
            + "\nRemote render LOD: " + RenderLodStatus()
            + "\nDeclutter: " + DeclutterStatus()
            + "\nRemote CPU budget: " + RemoteBudgetStatus()
            + "\nFrame pacing: " + _framePacing.StatusText
            + "\nCompatibility: " + _compatibility.StatusText;

        private void ApplyPresetIfChanged()
        {
            if (_configuration == null || _configuration.Preset.Value == _lastPreset) return;
            _lastPreset = _configuration.Preset.Value;
            if (_lastPreset == PerformancePreset.Custom) return;

            _configuration.KnownModFixesEnabled.Value = true;
            _configuration.FramePacingEnabled.Value = true;
            _configuration.RemoteUpdateBudgetEnabled.Value = true;
            _configuration.RemoteAnimatorCullingEnabled.Value = true;
            _configuration.AggressiveModeEnabled.Value = false;
            _configuration.RemoteAiRenderLodEnabled.Value = false;
            _configuration.SkinningEnabled.Value = false;

            if (_lastPreset == PerformancePreset.Balanced)
            {
                _configuration.RemoteUpdateBudgetDistance.Value = 60f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.3f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 2;
                _configuration.ShadowEnabled.Value = false;
                _configuration.CosmeticDeclutterEnabled.Value = false;
            }
            else if (_lastPreset == PerformancePreset.OldCpuAggressive)
            {
                _configuration.RemoteUpdateBudgetDistance.Value = 35f;
                _configuration.RemoteUpdateBudgetHold.Value = 0.15f;
                _configuration.RemoteUpdateBudgetDivisor.Value = 4;
                _configuration.ShadowEnabled.Value = true;
                _configuration.ShadowDistance.Value = 80f;
                _configuration.ShadowMinimumDistance.Value = 45f;
                _configuration.ShadowTargetFps.Value = 50f;
                _configuration.CosmeticDeclutterEnabled.Value = true;
            }
            Logger?.LogInfo("Applied performance preset: " + _lastPreset + ". LOD and texture quality changes remain disabled.");
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
