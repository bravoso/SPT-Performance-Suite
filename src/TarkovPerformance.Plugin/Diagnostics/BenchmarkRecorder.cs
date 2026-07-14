using System;
using System.Globalization;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using TarkovPerformanceSuite.Diagnostics;
using TarkovPerformanceSuite.RuntimeFeatures;

namespace TarkovPerformanceSuite.RuntimeDiagnostics
{
    internal sealed class BenchmarkRecorder
    {
        private readonly ManualLogSource _logger;
        private BenchmarkSample[] _buffer = Array.Empty<BenchmarkSample>();
        private int _count;
        private double _startedRealtime;
        private string _startedUtc;
        private string _map;
        private string _features;
        private string _directory;
        private bool _exportCsv;
        private double _sumFps;
        private double _minimumFps;
        private volatile string _completedMessage;

        internal BenchmarkRecorder(ManualLogSource logger) { _logger = logger; }
        internal bool IsCapturing { get; private set; }
        internal double DurationSeconds { get; private set; }
        internal double ElapsedSeconds { get; private set; }
        internal double AverageFps => _count > 0 ? _sumFps / _count : 0;
        internal double MinimumFps => _count > 0 ? _minimumFps : 0;

        internal void Start(double now, double durationSeconds, string map, string features, string directory, bool exportCsv)
        {
            if (IsCapturing) return;
            DurationSeconds = durationSeconds;
            int capacity = Math.Max(1, (int)Math.Ceiling(durationSeconds * 240.0));
            _buffer = new BenchmarkSample[capacity];
            _count = 0;
            _sumFps = 0;
            _minimumFps = double.MaxValue;
            _startedRealtime = now;
            _startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            _map = string.IsNullOrWhiteSpace(map) ? "unknown-map" : map;
            _features = features ?? string.Empty;
            _directory = directory;
            _exportCsv = exportCsv;
            ElapsedSeconds = 0;
            IsCapturing = true;
            _logger.LogInfo($"Benchmark capture started: {durationSeconds:F0} seconds, map {_map}, {_features}");
        }

        internal bool Record(double now, double frameTimeMs, ProfilerMetrics metrics, EntityCounts counts, ShadowFeatureCounters shadows,
            SkinningFeatureCounters skinning, RemoteRenderLodCounters renderLod, DeclutterCounters declutter, int? fikaServerFps)
        {
            if (!IsCapturing) return false;
            ElapsedSeconds = now - _startedRealtime;
            double fps = frameTimeMs > 0 ? 1000.0 / frameTimeMs : 0;
            if (_count < _buffer.Length)
            {
                _buffer[_count++] = new BenchmarkSample
                {
                    TimestampSeconds = ElapsedSeconds,
                    FrameTimeMs = frameTimeMs,
                    Fps = fps,
                    MainThreadMs = metrics.PreferredTimeMs("CPU Main Thread Frame Time", "Main Thread"),
                    RenderThreadMs = metrics.PreferredTimeMs("CPU Render Thread Frame Time", "Render Thread"),
                    CpuTotalMs = metrics.TimeMs("CPU Total Frame Time"),
                    GpuFrameMs = metrics.TimeMs("GPU Frame Time"),
                    FrameTimeGpuMs = metrics.TimeMs("FrameTime.GPU"),
                    GfxWaitForPresentMs = metrics.TimeMs("Gfx.WaitForPresentOnGfxThread"),
                    PlayerLoopMs = metrics.TimeMs("PlayerLoop"),
                    WaitForTargetFpsMs = metrics.TimeMs("WaitForTargetFPS"),
                    GcCollectMs = metrics.TimeMs("GC.Collect"),
                    GcValue = metrics.Value("GC Allocated In Frame") ?? metrics.Value("GC Reserved Memory"),
                    DrawCalls = metrics.Value("Draw Calls Count"),
                    SetPassCalls = metrics.Value("SetPass Calls Count"),
                    PlayerCount = counts.Players,
                    AiCount = counts.Ai,
                    VisibleAiCount = counts.VisibleAi,
                    CorpseCount = counts.Corpses,
                    AnimatorCount = counts.Animators,
                    SkinnedRendererCount = counts.SkinnedRenderers,
                    ShadowRendererCount = counts.ShadowRenderers,
                    ShadowEffectiveDistance = shadows.EffectiveDistance,
                    ShadowDisabledRendererCount = shadows.DisabledRenderers,
                    SkinningModifiedRendererCount = skinning.ModifiedRenderers,
                    RemoteLodMidAiCount = renderLod.MidTierAi,
                    RemoteLodFarAiCount = renderLod.FarTierAi,
                    RemoteLodForcedGroupCount = renderLod.ForcedLodGroups,
                    RemoteLodModifiedRendererCount = renderLod.ModifiedRenderers,
                    DeclutterHiddenRendererCount = declutter.Hidden,
                    FikaServerFps = fikaServerFps
                };
                _sumFps += fps;
                if (fps < _minimumFps) _minimumFps = fps;
            }
            if (ElapsedSeconds >= DurationSeconds)
            {
                Finish(false);
                return true;
            }
            return false;
        }

        internal void Finish(bool cancelled)
        {
            if (!IsCapturing) return;
            IsCapturing = false;
            BenchmarkSample[] samples = new BenchmarkSample[_count];
            Array.Copy(_buffer, samples, _count);
            _buffer = Array.Empty<BenchmarkSample>();
            if (cancelled) { _logger.LogInfo("Benchmark capture cancelled cleanly at raid end."); return; }

            var export = new BenchmarkExport { StartedUtc = _startedUtc, MapName = _map, EnabledFeatures = _features, Samples = samples };
            string directory = _directory;
            bool csv = _exportCsv;
            string stem = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_" + Sanitize(_map) + "_" + Sanitize(_features);
            ThreadPool.QueueUserWorkItem(_ => ExportWorker(export, directory, stem, csv));
        }

        internal void DrainCompletionLog()
        {
            string message = _completedMessage;
            if (message == null) return;
            _completedMessage = null;
            _logger.LogInfo(message);
        }

        private void ExportWorker(BenchmarkExport export, string directory, string stem, bool csv)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string jsonPath = Path.Combine(directory, stem + ".json");
                using (var writer = new StreamWriter(jsonPath, false, new System.Text.UTF8Encoding(false))) BenchmarkSerializer.WriteJson(writer, export);
                if (csv)
                {
                    using (var writer = new StreamWriter(Path.Combine(directory, stem + ".csv"), false, new System.Text.UTF8Encoding(false))) BenchmarkSerializer.WriteCsv(writer, export);
                }
                _completedMessage = "Benchmark export completed: " + jsonPath;
            }
            catch (Exception ex) { _completedMessage = "Benchmark export failed: " + ex; }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '-';
            string result = new string(chars);
            return result.Length > 80 ? result.Substring(0, 80) : result;
        }
    }
}
