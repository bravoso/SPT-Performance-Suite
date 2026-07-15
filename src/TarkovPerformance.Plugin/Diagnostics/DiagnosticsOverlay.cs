using System.Text;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Diagnostics;
using TarkovPerformanceSuite.FikaAdapter;
using TarkovPerformanceSuite.RuntimeFeatures;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeDiagnostics
{
    internal sealed class DiagnosticsOverlay
    {
        private readonly FrameStatistics _rolling = new FrameStatistics(600);
        private readonly StringBuilder _builder = new StringBuilder(2048);
        private GUIStyle _style;
        private string _text = string.Empty;
        private float _nextTextRefresh;

        internal bool Visible { get; set; }
        internal double SuiteAverageMs { get; set; }
        internal OverlayMode DisplayMode { get; set; } = OverlayMode.Compact;
        internal string PresetName { get; set; } = "Custom";
        internal bool OptimizationsActive { get; set; } = true;
        internal string OptimizationSummary { get; set; } = string.Empty;
        internal bool NeedsRefresh(float now) => Visible && now >= _nextTextRefresh;

        internal void AddFrame(double frameTimeMs) => _rolling.Add(frameTimeMs);

        internal void Refresh(float now, EntityCounts counts, ProfilerMetrics metrics, FikaDiagnosticsAdapter fika, BenchmarkRecorder capture, string raidState, string experimentStatus, string methodText)
        {
            if (!Visible || now < _nextTextRefresh) return;
            _nextTextRefresh = now + 0.25f;
            FrameStatisticsSnapshot stats = _rolling.Snapshot();
            double instantMs = Time.unscaledDeltaTime * 1000.0;
            double fps = instantMs > 0 ? 1000.0 / instantMs : 0;
            double onePercentLow = stats.P99Ms > 0 ? 1000.0 / stats.P99Ms : 0;
            fika.GetCounts(out int fikaObserved, out int fikaAi, out int fikaVisibleAi);
            double? mainMs = metrics.PreferredTimeMs("CPU Main Thread Frame Time", "Main Thread");
            double? renderMs = metrics.PreferredTimeMs("CPU Render Thread Frame Time", "Render Thread");
            double? gpuMs = metrics.PreferredTimeMs("GPU Frame Time", "FrameTime.GPU");
            string bottleneck = Bottleneck(mainMs, gpuMs);

            _builder.Clear();
            _builder.Append("TARKOV PERFORMANCE SUITE ").Append(Plugin.PluginVersion).Append("  |  ").Append(PresetName)
                .Append("  |  OPTIMIZATIONS ").Append(OptimizationsActive ? "ON" : "OFF").Append("  |  ").AppendLine(bottleneck);
            if (DisplayMode == OverlayMode.Compact)
            {
                _builder.Append("FPS ").Append(fps.ToString("F0")).Append("   1% low ").Append(onePercentLow.ToString("F0"))
                    .Append("   frame ").Append(instantMs.ToString("F2")).AppendLine(" ms");
                _builder.Append("CPU main ").Append(Format(mainMs)).Append(" ms   render ").Append(Format(renderMs)).Append(" ms   GPU ").Append(Format(gpuMs)).AppendLine(" ms");
                _builder.Append("AI ").Append(counts.Ai).Append(" (visible ").Append(counts.VisibleAi).Append(", map-hidden ").Append(counts.BakedHiddenEntities).Append(")   players ").Append(counts.Players)
                    .Append("   server ").Append(fika.ServerFps?.ToString() ?? "n/a").AppendLine(" FPS");
                _builder.Append("Capture: ").Append(capture.IsCapturing ? capture.ElapsedSeconds.ToString("F0") + "/" + capture.DurationSeconds.ToString("F0") + " s" : "idle")
                    .Append("   Suite cost ").Append(SuiteAverageMs.ToString("F3")).AppendLine(" ms");
                _builder.AppendLine("Num4 all optimizations A/B   Num7 overlay   Num8 reports   Num9 report   F12 settings");
                _text = _builder.ToString();
                return;
            }
            _builder.AppendLine("[Num4 ALL OPTIMIZATIONS A/B | Num7 overlay | Num8 repeat reports | Num9 report | F12 settings]");
            _builder.Append("State: ").Append(raidState).Append(" | Experiments: ").AppendLine(experimentStatus);
            _builder.Append("FPS ").Append(fps.ToString("F1")).Append(" | frame ").Append(instantMs.ToString("F2")).Append(" ms | avg ").Append(stats.AverageMs.ToString("F2"))
                .Append(" | median ").Append(stats.MedianMs.ToString("F2")).Append(" | p95 ").Append(stats.P95Ms.ToString("F2")).Append(" | p99 ").Append(stats.P99Ms.ToString("F2")).AppendLine(" ms");
            _builder.Append("Capture: ").Append(capture.IsCapturing ? capture.ElapsedSeconds.ToString("F1") + "/" + capture.DurationSeconds.ToString("F0") + " s" : "idle")
                .Append(" | min FPS ").Append(capture.MinimumFps.ToString("F1")).Append(" | avg FPS ").Append(capture.AverageFps.ToString("F1")).AppendLine();
            AppendMetric("CPU main", mainMs, "ms");
            AppendMetric("CPU render", renderMs, "ms");
            AppendMetric("CPU total", metrics.TimeMs("CPU Total Frame Time"), "ms");
            AppendMetric("GPU", gpuMs, "ms");
            _builder.AppendLine();
            AppendMetric("PlayerLoop", metrics.TimeMs("PlayerLoop"), "ms");
            AppendMetric("present wait", metrics.TimeMs("Gfx.WaitForPresentOnGfxThread"), "ms");
            AppendMetric("FPS-limit wait", metrics.TimeMs("WaitForTargetFPS"), "ms");
            AppendMetric("GC.Collect", metrics.TimeMs("GC.Collect"), "ms");
            _builder.AppendLine();
            AppendMemory("managed reserve", metrics.Value("GC Reserved Memory")); AppendMemory("managed used", metrics.Value("GC Used Memory")); AppendMemory("system", metrics.Value("System Used Memory"));
            _builder.AppendLine();
            AppendLong("draw", metrics.Value("Draw Calls Count")); AppendLong("batches", metrics.Value("Batches Count")); AppendLong("SetPass", metrics.Value("SetPass Calls Count"));
            _builder.AppendLine();
            AppendLong("triangles", metrics.Value("Triangles Count")); AppendLong("vertices", metrics.Value("Vertices Count")); AppendLong("shadow casters", metrics.Value("Shadow Casters Count"));
            _builder.AppendLine();
            _builder.Append("Entities: players ").Append(counts.Players).Append(" (local ").Append(counts.LocalPlayers).Append(", human ").Append(counts.RemoteHumans)
                .Append(") | AI ").Append(counts.Ai).Append(" living ").Append(counts.LivingAi).Append(" visible ").Append(counts.VisibleAi)
                .Append(" | baked culling ").Append(counts.BakedCullingEntities).Append(" hidden ").Append(counts.BakedHiddenEntities)
                .Append(" | observed corpses ").Append(counts.Corpses).AppendLine();
            _builder.Append("Presentation: animators ").Append(counts.Animators).Append(" | skinned renderers ").Append(counts.SkinnedRenderers).Append(" | AI shadow renderers ").Append(counts.ShadowRenderers).AppendLine();
            _builder.Append("Fika: observed ").Append(fikaObserved).Append(" | observed AI ").Append(fikaAi).Append(" | locally visible AI ").Append(fikaVisibleAi)
                .Append(" | server FPS ").Append(fika.ServerFps?.ToString() ?? "n/a").AppendLine();
            _builder.Append("Suite Update average: ").Append(SuiteAverageMs.ToString("F3")).AppendLine(" ms");
            if (!string.IsNullOrEmpty(OptimizationSummary)) _builder.AppendLine(OptimizationSummary);
            if (!string.IsNullOrEmpty(methodText)) _builder.AppendLine(methodText);
            _text = _builder.ToString();
        }

        internal void Draw()
        {
            if (!Visible) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };
                _style.normal.textColor = Color.white;
            }
            float width = DisplayMode == OverlayMode.Compact ? Mathf.Min(720, Screen.width - 24) : Mathf.Min(1120, Screen.width - 24);
            float height = DisplayMode == OverlayMode.Compact ? 145 : Mathf.Min(650, Screen.height - 24);
            GUI.Box(new Rect(12, 12, width, height), _text, _style);
        }

        internal void Reset()
        {
            _rolling.Clear();
            _text = string.Empty;
            _nextTextRefresh = 0;
        }

        private void AppendMetric(string label, double? value, string unit)
        {
            _builder.Append(label).Append(' ').Append(value.HasValue ? value.Value.ToString("F2") : "n/a").Append(' ').Append(unit).Append(" | ");
        }
        private void AppendLong(string label, long? value)
        {
            _builder.Append(label).Append(' ').Append(value?.ToString() ?? "n/a").Append(" | ");
        }
        private void AppendMemory(string label, long? value)
        {
            _builder.Append(label).Append(' ').Append(value.HasValue ? (value.Value / 1048576.0d).ToString("F1") : "n/a").Append(" MiB | ");
        }

        private static string Format(double? value) => value.HasValue ? value.Value.ToString("F2") : "n/a";

        private static string Bottleneck(double? mainMs, double? gpuMs)
        {
            if (!mainMs.HasValue || !gpuMs.HasValue || mainMs.Value <= 0 || gpuMs.Value <= 0) return "MEASURING";
            if (mainMs.Value > gpuMs.Value * 1.2) return "CPU BOTTLENECK";
            if (gpuMs.Value > mainMs.Value * 1.2) return "GPU BOTTLENECK";
            return "BALANCED";
        }
    }
}
