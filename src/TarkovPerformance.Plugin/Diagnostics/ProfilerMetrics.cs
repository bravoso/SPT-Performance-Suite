using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace TarkovPerformanceSuite.RuntimeDiagnostics
{
    internal sealed class ProfilerMetric : IDisposable
    {
        private ProfilerRecorder _recorder;
        internal ProfilerMetric(string name, ProfilerRecorderHandle handle)
        {
            Name = name;
            _recorder = new ProfilerRecorder(handle, 1);
            _recorder.Start();
        }
        internal string Name { get; }
        internal bool Valid => _recorder.Valid;
        internal long? LastValue => _recorder.Valid && _recorder.Count > 0 ? _recorder.LastValue : (long?)null;
        public void Dispose() { if (_recorder.Valid) _recorder.Dispose(); }
    }

    internal sealed class ProfilerMetrics : IDisposable
    {
        private static readonly string[] DesiredNames =
        {
            "Main Thread", "Render Thread", "GC Allocated In Frame", "GC Reserved Memory", "System Used Memory",
            "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count"
        };

        private readonly Dictionary<string, ProfilerMetric> _metrics = new Dictionary<string, ProfilerMetric>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>(256);
        private readonly List<string> _available = new List<string>(256);
        private readonly ManualLogSource _logger;

        internal ProfilerMetrics(ManualLogSource logger) { _logger = logger; }
        internal IReadOnlyList<string> Available => _available;

        internal void Start()
        {
            Dispose();
            _handles.Clear();
            _available.Clear();
            try
            {
                ProfilerRecorderHandle.GetAvailable(_handles);
                for (int i = 0; i < _handles.Count; i++)
                {
                    ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(_handles[i]);
                    string name = description.Name;
                    _available.Add(description.Category.Name + " | " + name + " | " + description.UnitType);
                    if (!IsDesired(name) || _metrics.ContainsKey(name)) continue;
                    var metric = new ProfilerMetric(name, _handles[i]);
                    if (metric.Valid) _metrics.Add(name, metric); else metric.Dispose();
                }
                _available.Sort(StringComparer.OrdinalIgnoreCase);
                _logger.LogInfo($"Profiler metrics: {_available.Count} available, {_metrics.Count} selected.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("ProfilerRecorder enumeration is unavailable: " + ex.Message);
                Dispose();
            }
        }

        internal long? Value(string name) => _metrics.TryGetValue(name, out ProfilerMetric metric) ? metric.LastValue : null;
        internal double? TimeMs(string name)
        {
            long? value = Value(name);
            return value.HasValue ? value.Value * 0.000001d : (double?)null;
        }

        internal void WriteAvailableReport(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# Runtime available profiler metrics");
                writer.WriteLine();
                writer.WriteLine("Generated inside Unity. Availability varies by player build and enabled profiler counters.");
                writer.WriteLine();
                for (int i = 0; i < _available.Count; i++) writer.WriteLine("- " + _available[i]);
            }
        }

        public void Dispose()
        {
            foreach (ProfilerMetric metric in _metrics.Values) metric.Dispose();
            _metrics.Clear();
        }

        private static bool IsDesired(string name)
        {
            for (int i = 0; i < DesiredNames.Length; i++) if (string.Equals(name, DesiredNames[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

