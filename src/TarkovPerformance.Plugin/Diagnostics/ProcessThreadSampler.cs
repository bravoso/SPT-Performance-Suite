using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TarkovPerformanceSuite.RuntimeDiagnostics;

/// <summary>Samples per-thread CPU deltas at a low rate to distinguish main-thread and worker pressure.</summary>
internal sealed class ProcessThreadSampler
{
    private readonly Dictionary<int, ThreadSample> _threads = new Dictionary<int, ThreadSample>();
    private readonly int _logicalProcessors = Math.Max(1, Environment.ProcessorCount);
    private readonly int _mainOsThreadId;
    private double _lastRealtime;
    private double _nextSample;
    private double _observedWallSeconds;
    private double _totalCpuMilliseconds;
    private int _sampleCount;
    private long _successfulReads;
    private long _failedReads;
    private string _lastError;

    internal ProcessThreadSampler()
    {
        try
        {
            _mainOsThreadId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? unchecked((int)GetCurrentThreadId()) : -1;
        }
        catch
        {
            _mainOsThreadId = -1;
        }
    }

    internal void Reset(double now)
    {
        _threads.Clear();
        _lastRealtime = now;
        _nextSample = now + 1.0;
        _observedWallSeconds = 0;
        _totalCpuMilliseconds = 0;
        _sampleCount = 0;
        _successfulReads = 0;
        _failedReads = 0;
        _lastError = null;
        Capture(now, baselineOnly: true);
    }

    internal void Tick(double now)
    {
        if (now < _nextSample)
        {
            return;
        }

        Capture(now, baselineOnly: false);
        _nextSample = now + 1.0;
    }

    internal string BuildReport()
    {
        var ranked = new List<ThreadSample>(_threads.Values);
        ranked.Sort((left, right) => right.AccumulatedCpuMs.CompareTo(left.AccumulatedCpuMs));
        var builder = new StringBuilder(2048);
        builder.AppendLine("Process CPU thread sample (approximately once per second)");
        builder.AppendLine(
            "Logical processors: "
                + _logicalProcessors
                + " | samples: "
                + _sampleCount
                + " | observed wall time: "
                + _observedWallSeconds.ToString("F2", CultureInfo.InvariantCulture)
                + " s"
        );
        double oneCoreScale = WholeProcessPercent();
        builder.AppendLine(
            "CPU time observed: "
                + _totalCpuMilliseconds.ToString("F2", CultureInfo.InvariantCulture)
                + " ms | core-equivalent CPU: "
                + oneCoreScale.ToString("F1", CultureInfo.InvariantCulture)
                + "% | Task-Manager-style CPU: "
                + (oneCoreScale / _logicalProcessors).ToString("F1", CultureInfo.InvariantCulture)
                + "%"
        );
        builder.AppendLine(
            "A thread near 100% of one core is saturated. Core-equivalent process CPU may exceed 100% when multiple cores are busy."
        );
        builder.AppendLine("Native thread reads: " + _successfulReads + " successful | " + _failedReads + " failed");
        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            builder.AppendLine("Sampler warning: " + _lastError);
        }

        builder.AppendLine("rank | OS thread | role | CPU ms | one-core % | peak interval % | intervals");
        int shown = Math.Min(24, ranked.Count);
        for (int i = 0; i < shown; i++)
        {
            ThreadSample sample = ranked[i];
            if (sample.AccumulatedCpuMs <= 0)
            {
                continue;
            }

            double oneCore = _observedWallSeconds > 0 ? sample.AccumulatedCpuMs / (_observedWallSeconds * 10.0) : 0;
            builder
                .Append(i + 1)
                .Append(" | ")
                .Append(sample.Id)
                .Append(" | ")
                .Append(sample.Id == _mainOsThreadId ? "Unity main" : "worker/native")
                .Append(" | ")
                .Append(sample.AccumulatedCpuMs.ToString("F2", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(oneCore.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(sample.PeakOneCorePercent.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(sample.Intervals)
                .AppendLine();
        }
        return builder.ToString();
    }

    private void Capture(double now, bool baselineOnly)
    {
        double wallSeconds = Math.Max(0, now - _lastRealtime);
        try
        {
            using (Process process = Process.GetCurrentProcess())
            {
                var seen = new HashSet<int>();
                foreach (ProcessThread thread in process.Threads)
                {
                    try
                    {
                        int id = thread.Id;
                        double currentMs = ReadThreadCpuMilliseconds(thread);
                        _successfulReads++;
                        seen.Add(id);
                        if (!_threads.TryGetValue(id, out ThreadSample sample))
                        {
                            sample = new ThreadSample { Id = id, LastCpuMs = currentMs };
                            _threads.Add(id, sample);
                            continue;
                        }
                        double delta = currentMs - sample.LastCpuMs;
                        sample.LastCpuMs = currentMs;
                        if (!baselineOnly && delta >= 0 && delta < 60000)
                        {
                            sample.AccumulatedCpuMs += delta;
                            sample.Intervals++;
                            double intervalPercent = wallSeconds > 0 ? delta / (wallSeconds * 10.0) : 0;
                            if (intervalPercent > sample.PeakOneCorePercent)
                            {
                                sample.PeakOneCorePercent = intervalPercent;
                            }

                            _totalCpuMilliseconds += delta;
                        }
                    }
                    catch (Exception ex)
                    {
                        _failedReads++;
                        if (string.IsNullOrWhiteSpace(_lastError))
                        {
                            _lastError = ex.GetType().Name + ": " + ex.Message;
                        }
                    }
                }
            }
            if (!baselineOnly)
            {
                _observedWallSeconds += wallSeconds;
                _sampleCount++;
            }
            _lastRealtime = now;
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _lastRealtime = now;
        }
    }

    private double WholeProcessPercent()
    {
        return _observedWallSeconds > 0 ? _totalCpuMilliseconds / (_observedWallSeconds * 10.0) : 0;
    }

    private static double ReadThreadCpuMilliseconds(ProcessThread thread)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return thread.TotalProcessorTime.TotalMilliseconds;
        }

        IntPtr handle = OpenThread(ThreadQueryLimitedInformation, false, unchecked((uint)thread.Id));
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("OpenThread failed for " + thread.Id + ".");
        }

        try
        {
            if (
                !GetThreadTimes(
                    handle,
                    out NativeFileTime created,
                    out NativeFileTime exited,
                    out NativeFileTime kernel,
                    out NativeFileTime user
                )
            )
            {
                throw new InvalidOperationException("GetThreadTimes failed for " + thread.Id + ".");
            }

            ulong kernelTicks = ((ulong)kernel.High << 32) | kernel.Low;
            ulong userTicks = ((ulong)user.High << 32) | user.Low;
            return (kernelTicks + userTicks) / 10000.0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Stores the previous native CPU counter for one operating-system thread.</summary>
    private sealed class ThreadSample
    {
        internal int Id;
        internal double LastCpuMs;
        internal double AccumulatedCpuMs;
        internal double PeakOneCorePercent;
        internal int Intervals;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint ThreadQueryLimitedInformation = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    /// <summary>Matches the Windows FILETIME layout used by the thread timing interop call.</summary>
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadTimes(
        IntPtr threadHandle,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime
    );

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
