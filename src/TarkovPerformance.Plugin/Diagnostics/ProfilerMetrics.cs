using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Logging;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace TarkovPerformanceSuite.RuntimeDiagnostics;

/// <summary>Wraps one Unity profiler recorder and exposes allocation-free current values.</summary>
internal sealed class ProfilerMetric : IDisposable
{
    private ProfilerRecorder _recorder;

    internal ProfilerMetric(string name, ProfilerRecorderHandle handle, ProfilerMarkerDataUnit unitType)
    {
        Name = name;
        UnitType = unitType;
        _recorder = new ProfilerRecorder(handle, 1);
        _recorder.Start();
    }

    internal string Name { get; }
    internal ProfilerMarkerDataUnit UnitType { get; }
    internal bool Valid
    {
        get { return _recorder.Valid; }
    }

    internal long? LastValue
    {
        get { return _recorder.Valid && _recorder.Count > 0 ? _recorder.LastValue : (long?)null; }
    }

    internal long CaptureSamples { get; private set; }
    internal double CaptureSum { get; private set; }
    internal long CaptureMaximum { get; private set; }

    internal void ResetCapture()
    {
        CaptureSamples = 0;
        CaptureSum = 0;
        CaptureMaximum = 0;
    }

    internal void SampleCapture()
    {
        long? value = LastValue;
        if (!value.HasValue)
        {
            return;
        }

        CaptureSamples++;
        CaptureSum += value.Value;
        if (value.Value > CaptureMaximum)
        {
            CaptureMaximum = value.Value;
        }
    }

    public void Dispose()
    {
        if (_recorder.Valid)
        {
            _recorder.Dispose();
        }
    }
}

/// <summary>Owns the optional Unity profiler recorders used in reports and the diagnostics overlay.</summary>
internal sealed class ProfilerMetrics : IDisposable
{
    private static readonly string[] DesiredNames =
    {
        "Main Thread",
        "Render Thread",
        "GC Allocated In Frame",
        "GC Reserved Memory",
        "System Used Memory",
        "GC Used Memory",
        "App Resident Memory",
        "App Committed Memory",
        "Video Used Memory",
        "Video Reserved Memory",
        "Video Memory Bytes",
        "Render Textures Bytes",
        "Render Textures Count",
        "Used Buffers Bytes",
        "Used Buffers Count",
        "Draw Calls Count",
        "Batches Count",
        "SetPass Calls Count",
        "Triangles Count",
        "Vertices Count",
        "Shadow Casters Count",
        "Visible Skinned Meshes Count",
    };

    private readonly Dictionary<string, ProfilerMetric> _metrics = new Dictionary<string, ProfilerMetric>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProfilerRecorderHandle> _handles = new List<ProfilerRecorderHandle>(256);
    private readonly List<string> _available = new List<string>(256);
    private readonly ManualLogSource _logger;

    internal ProfilerMetrics(ManualLogSource logger)
    {
        _logger = logger;
    }

    internal IReadOnlyList<string> Available
    {
        get { return _available; }
    }

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
                if (!IsDesired(name, description.UnitType) || _metrics.ContainsKey(name))
                {
                    continue;
                }

                var metric = new ProfilerMetric(name, _handles[i], description.UnitType);
                if (metric.Valid)
                {
                    _metrics.Add(name, metric);
                }
                else
                {
                    metric.Dispose();
                }
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

    internal long? Value(string name)
    {
        return _metrics.TryGetValue(name, out ProfilerMetric metric) ? metric.LastValue : null;
    }

    internal double? TimeMs(string name)
    {
        long? value = Value(name);
        return value.HasValue ? value.Value * 0.000001d : (double?)null;
    }

    internal double? PreferredTimeMs(string primary, string fallback)
    {
        double? primaryValue = TimeMs(primary);
        return primaryValue ?? TimeMs(fallback);
    }

    internal void AppendCurrentSnapshot(StringBuilder builder)
    {
        foreach (ProfilerMetric metric in _metrics.Values)
        {
            long? value = metric.LastValue;
            builder.Append(metric.Name).Append(" = ");
            if (!value.HasValue)
            {
                builder.AppendLine("n/a");
            }
            else if (metric.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                builder.Append((value.Value * 0.000001d).ToString("F4")).AppendLine(" ms");
            }
            else if (metric.UnitType == ProfilerMarkerDataUnit.Bytes)
            {
                builder.Append((value.Value / 1048576.0d).ToString("F2")).AppendLine(" MiB");
            }
            else
            {
                builder.Append(value.Value).AppendLine(" " + metric.UnitType);
            }
        }
    }

    internal void BeginCapture()
    {
        foreach (ProfilerMetric metric in _metrics.Values)
        {
            metric.ResetCapture();
        }
    }

    internal void SampleCapture()
    {
        foreach (ProfilerMetric metric in _metrics.Values)
        {
            metric.SampleCapture();
        }
    }

    internal string BuildCumulativeReport()
    {
        var metrics = new List<ProfilerMetric>(_metrics.Values);
        metrics.Sort((left, right) => right.CaptureSum.CompareTo(left.CaptureSum));
        var builder = new StringBuilder(4096);
        builder.AppendLine("Unity profiler markers accumulated across captured frames");
        builder.AppendLine(
            "Timing totals are sums of the marker's per-frame value. Unity markers can be nested, so do not add rows together."
        );
        builder.AppendLine("metric | unit | samples | average | maximum | cumulative");
        for (int i = 0; i < metrics.Count; i++)
        {
            ProfilerMetric metric = metrics[i];
            if (metric.CaptureSamples == 0)
            {
                continue;
            }

            double average = metric.CaptureSum / metric.CaptureSamples;
            builder.Append(metric.Name).Append(" | ").Append(metric.UnitType).Append(" | ").Append(metric.CaptureSamples).Append(" | ");
            if (metric.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds)
            {
                builder
                    .Append((average * 0.000001d).ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" ms | ")
                    .Append((metric.CaptureMaximum * 0.000001d).ToString("F4", CultureInfo.InvariantCulture))
                    .Append(" ms | ")
                    .Append((metric.CaptureSum * 0.000001d).ToString("F2", CultureInfo.InvariantCulture))
                    .AppendLine(" ms");
            }
            else if (metric.UnitType == ProfilerMarkerDataUnit.Bytes)
            {
                builder
                    .Append((average / 1048576.0d).ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" MiB | ")
                    .Append((metric.CaptureMaximum / 1048576.0d).ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" MiB | n/a")
                    .AppendLine();
            }
            else
            {
                builder
                    .Append(average.ToString("F2", CultureInfo.InvariantCulture))
                    .Append(" | ")
                    .Append(metric.CaptureMaximum)
                    .Append(" | ")
                    .Append(metric.CaptureSum.ToString("F0", CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }
        return builder.ToString();
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
            for (int i = 0; i < _available.Count; i++)
            {
                writer.WriteLine("- " + _available[i]);
            }
        }
    }

    public void Dispose()
    {
        foreach (ProfilerMetric metric in _metrics.Values)
        {
            metric.Dispose();
        }

        _metrics.Clear();
    }

    private static bool IsDesired(string name, ProfilerMarkerDataUnit unitType)
    {
        if (
            unitType == ProfilerMarkerDataUnit.TimeNanoseconds
            && !string.Equals(name, "<Uninitialized ProfilerMarker>", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        for (int i = 0; i < DesiredNames.Length; i++)
        {
            if (string.Equals(name, DesiredNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
