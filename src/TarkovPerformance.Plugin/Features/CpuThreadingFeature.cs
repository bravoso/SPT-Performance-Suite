using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Raises safe worker-thread floors and restores process settings when the plugin shuts down.</summary>
internal sealed class CpuThreadingFeature : IPerformanceFeature
{
    private const ushort AllProcessorGroups = 0xffff;
    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private IntPtr _originalAffinity;
    private IntPtr _targetAffinity;
    private bool _haveOriginal;
    private bool _raidActive;
    private float _nextApply;
    private int _detectedLogicalProcessors;
    private int _jobWorkers = -1;
    private int _maximumJobWorkers = -1;

    internal CpuThreadingFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "CPU Logical Processor Access"; }
    }

    public bool IsAvailable
    {
        get { return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal string StatusText
    {
        get
        {
            if (_breaker.IsOpen)
            {
                return "disabled (circuit breaker)";
            }

            if (!IsAvailable)
            {
                return "unavailable on this platform";
            }

            if (!IsEnabled)
            {
                return "disabled";
            }

            int original = CountBits(_originalAffinity);
            int target = CountBits(_targetAffinity);
            string effect = _haveOriginal && original < target ? "expanded" : "already unrestricted";
            string workers = _jobWorkers >= 0 ? $" | Unity workers {_jobWorkers}/{_maximumJobWorkers}" : string.Empty;
            return $"enabled | {effect} | original {original} logical | target {target} of {_detectedLogicalProcessors}{workers}";
        }
    }

    public void Initialize()
    {
        _breaker.Reset();
        SetEnabled(_configuration.UseAllLogicalProcessors.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        _nextApply = 0;
        if (IsEnabled)
        {
            Apply();
        }
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        Restore();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
        {
            return;
        }

        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        _configuration.UseAllLogicalProcessors.Value = enabled;
        if (enabled && _raidActive)
        {
            Apply();
        }
        else if (!enabled)
        {
            Restore();
        }
    }

    internal void Tick(float now)
    {
        if (!IsEnabled || !_raidActive || now < _nextApply)
        {
            return;
        }

        _nextApply = now + 2f;
        Apply();
    }

    public void Shutdown()
    {
        _raidActive = false;
        IsEnabled = false;
        Restore();
    }

    private void Apply()
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
            {
                if (!_haveOriginal)
                {
                    _originalAffinity = process.ProcessorAffinity;
                    _detectedLogicalProcessors = DetectLogicalProcessorCount();
                    _targetAffinity = CreateAffinityMask(_detectedLogicalProcessors);
                    _haveOriginal = true;
                    _logger.LogInfo(
                        $"CPU affinity experiment: original mask exposes {CountBits(_originalAffinity)} logical processors; all-logical mask exposes {CountBits(_targetAffinity)}."
                    );
                }
                if (_targetAffinity != IntPtr.Zero && process.ProcessorAffinity != _targetAffinity)
                {
                    process.ProcessorAffinity = _targetAffinity;
                }
            }
            ReadUnityWorkerCounts();
            _breaker.Success();
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name, ex);
            _logger.LogWarning(Name + " failed open: " + ex.Message);
            if (_breaker.Failure())
            {
                IsEnabled = false;
                _configuration.UseAllLogicalProcessors.Value = false;
                Restore();
            }
        }
    }

    private void Restore()
    {
        if (!_haveOriginal)
        {
            return;
        }

        try
        {
            using (Process process = Process.GetCurrentProcess())
            {
                process.ProcessorAffinity = _originalAffinity;
            }
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name + " restore", ex);
        }
        _haveOriginal = false;
        _originalAffinity = IntPtr.Zero;
        _targetAffinity = IntPtr.Zero;
    }

    private static int DetectLogicalProcessorCount()
    {
        try
        {
            uint count = GetActiveProcessorCount(AllProcessorGroups);
            if (count > 0)
            {
                return (int)Math.Min(count, IntPtr.Size * 8);
            }
        }
        catch { }
        return Math.Min(Environment.ProcessorCount, IntPtr.Size * 8);
    }

    private static IntPtr CreateAffinityMask(int logicalProcessors)
    {
        int width = Math.Min(Math.Max(logicalProcessors, 1), IntPtr.Size * 8);
        ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1UL;
        return new IntPtr(unchecked((long)mask));
    }

    private static int CountBits(IntPtr mask)
    {
        ulong value = unchecked((ulong)mask.ToInt64());
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }

    private void ReadUnityWorkerCounts()
    {
        try
        {
            Type jobs = Type.GetType("Unity.Jobs.LowLevel.Unsafe.JobsUtility, UnityEngine.CoreModule", false);
            if (jobs == null)
            {
                return;
            }

            _jobWorkers = ReadStaticInt(jobs, "JobWorkerCount");
            _maximumJobWorkers = ReadStaticInt(jobs, "JobWorkerMaximumCount");
        }
        catch { }
    }

    private static int ReadStaticInt(Type type, string name)
    {
        PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.PropertyType == typeof(int))
        {
            return (int)property.GetValue(null, null);
        }

        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null && field.FieldType == typeof(int) ? (int)field.GetValue(null) : -1;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);
}
