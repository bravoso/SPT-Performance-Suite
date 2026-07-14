using System;

namespace TarkovPerformanceSuite.Diagnostics
{
    public readonly struct FrameStatisticsSnapshot
    {
        public FrameStatisticsSnapshot(int count, double averageMs, double medianMs, double p95Ms, double p99Ms, double minimumFps)
        {
            Count = count;
            AverageMs = averageMs;
            MedianMs = medianMs;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            MinimumFps = minimumFps;
        }

        public int Count { get; }
        public double AverageMs { get; }
        public double MedianMs { get; }
        public double P95Ms { get; }
        public double P99Ms { get; }
        public double MinimumFps { get; }
    }

    public sealed class FrameStatistics
    {
        private readonly double[] _samples;
        private readonly double[] _scratch;
        private int _next;
        private int _count;

        public FrameStatistics(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new double[capacity];
            _scratch = new double[capacity];
        }

        public int Count => _count;

        public void Add(double frameTimeMs)
        {
            if (frameTimeMs <= 0 || double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs)) return;
            _samples[_next] = frameTimeMs;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
        }

        public FrameStatisticsSnapshot Snapshot()
        {
            if (_count == 0) return default;
            double sum = 0;
            double maximumMs = 0;
            for (int i = 0; i < _count; i++)
            {
                double value = _samples[i];
                _scratch[i] = value;
                sum += value;
                if (value > maximumMs) maximumMs = value;
            }

            Array.Sort(_scratch, 0, _count);
            return new FrameStatisticsSnapshot(
                _count,
                sum / _count,
                Percentiles.FromSorted(_scratch, _count, 0.50),
                Percentiles.FromSorted(_scratch, _count, 0.95),
                Percentiles.FromSorted(_scratch, _count, 0.99),
                maximumMs > 0 ? 1000.0 / maximumMs : 0);
        }
    }

    public static class Percentiles
    {
        public static double FromSorted(double[] sortedValues, int count, double percentile)
        {
            if (sortedValues == null) throw new ArgumentNullException(nameof(sortedValues));
            if (count <= 0 || count > sortedValues.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (percentile < 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));

            double rank = percentile * (count - 1);
            int lower = (int)rank;
            int upper = lower + 1;
            if (upper >= count) return sortedValues[lower];
            double fraction = rank - lower;
            return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
        }
    }
}

