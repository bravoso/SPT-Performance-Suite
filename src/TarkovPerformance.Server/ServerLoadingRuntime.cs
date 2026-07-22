using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers.Http;
using TarkovPerformanceSuite.Server.Configuration;
using TarkovPerformanceSuite.Server.Diagnostics;
using TarkovPerformanceSuite.Server.Lifecycle;
using TarkovPerformanceSuite.Server.Patches;

namespace TarkovPerformanceSuite.Server;

/// <summary>
/// Owns process-wide state for the optional SPT server loading component.
/// </summary>
/// <remarks>
/// The server component can influence startup and HTTP response cost, but it does not run EFT raid simulation and
/// therefore cannot directly improve client frame rate. Request samples are bounded and reporting is disabled by
/// default so diagnostic work does not become a permanent server hot path.
/// </remarks>
internal static class ServerLoadingRuntime
{
    private const int MaximumRetainedRequests = 4096;
    private const double MinimumReportIntervalMs = 10_000d;

    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();
    private static readonly ConcurrentQueue<RequestSample> Requests = new();
    private static readonly string ModRoot =
        Path.GetDirectoryName(typeof(ServerLoadingRuntime).Assembly.Location) ?? AppContext.BaseDirectory;
    private static readonly object ReportLock = new();

    private static ServerLoadingConfiguration _configuration = new();
    private static Harmony? _harmony;
    private static double _databaseReadyMs;
    private static double _serverReadyMs;
    private static long _lastReportTimestamp;
    private static int _requestCount;

    internal static bool FastCompressionEnabled => _configuration.FastCompression;

    internal static void Initialize(ISptLogger<LoadingServerBootstrap> logger)
    {
        _configuration = LoadConfiguration(logger);
        ThreadPool.GetMinThreads(out int originalWorkers, out int originalIo);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        int desiredWorkers = Math.Min(
            maxWorkers,
            Math.Max(originalWorkers, Environment.ProcessorCount * Math.Clamp(_configuration.WorkerThreadsPerLogicalCpu, 1, 4))
        );
        bool threadPoolChanged = ThreadPool.SetMinThreads(desiredWorkers, originalIo);

        if (_configuration.RaiseProcessPriority)
        {
            TryRaiseProcessPriority(logger);
        }

        _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.loadingserver");
        if (_configuration.WriteReports)
        {
            PatchRequestTiming();
        }

        if (_configuration.FastCompression)
        {
            PatchFastCompression();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteReport("server-shutdown");

        logger.Info(
            "Loading accelerator active: worker floor "
                + originalWorkers
                + " -> "
                + desiredWorkers
                + " (applied="
                + threadPoolChanged
                + "), fast zlib="
                + _configuration.FastCompression
                + "."
        );
    }

    internal static void MarkDatabaseReady<T>(ISptLogger<T> logger)
    {
        if (!_configuration.WriteReports)
        {
            return;
        }

        _databaseReadyMs = Lifetime.Elapsed.TotalMilliseconds;
        logger.Info(
            "Loading accelerator: database-ready checkpoint at " + _databaseReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms."
        );
    }

    internal static void MarkServerReady<T>(ISptLogger<T> logger)
    {
        if (!_configuration.WriteReports)
        {
            return;
        }

        _serverReadyMs = Lifetime.Elapsed.TotalMilliseconds;
        logger.Info(
            "Loading accelerator: server-ready checkpoint at " + _serverReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms."
        );
        WriteReport("server-ready");
    }

    internal static void RecordRequest(string method, string path, long started, Exception? error)
    {
        double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        Requests.Enqueue(new RequestSample(method, path, elapsedMs, error?.GetType().Name));
        int count = Interlocked.Increment(ref _requestCount);
        while (count > MaximumRetainedRequests && Requests.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _requestCount);
        }

        bool important =
            elapsedMs >= _configuration.SlowRequestThresholdMs
            || path.Contains("/profile/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/location/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/fika/raid/", StringComparison.OrdinalIgnoreCase);
        if (important)
        {
            ScheduleReport();
        }
    }

    private static void TryRaiseProcessPriority(ISptLogger<LoadingServerBootstrap> logger)
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
        }
        catch (Exception ex)
        {
            // Priority changes may be rejected by Windows policy. Startup must continue at normal priority.
            logger.Warning("Could not set above-normal server priority: " + ex.Message);
        }
    }

    private static void PatchRequestTiming()
    {
        MethodInfo target = AccessTools.Method(typeof(SptHttpListener), nameof(SptHttpListener.Handle));
        _harmony!.Patch(
            target,
            prefix: new HarmonyMethod(typeof(ServerRequestPatches), nameof(ServerRequestPatches.HandlePrefix)),
            postfix: new HarmonyMethod(typeof(ServerRequestPatches), nameof(ServerRequestPatches.HandlePostfix))
        );
    }

    private static void PatchFastCompression()
    {
        MethodInfo target = AccessTools.Method(typeof(SptHttpListener), nameof(SptHttpListener.SendZlibJson));
        _harmony!.Patch(target, prefix: new HarmonyMethod(typeof(ServerRequestPatches), nameof(ServerRequestPatches.SendZlibJsonPrefix)));
    }

    private static ServerLoadingConfiguration LoadConfiguration(ISptLogger<LoadingServerBootstrap> logger)
    {
        string path = Path.Combine(ModRoot, "config.json");
        try
        {
            if (!File.Exists(path))
            {
                return new ServerLoadingConfiguration();
            }

            return JsonSerializer.Deserialize<ServerLoadingConfiguration>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    }
                ) ?? new ServerLoadingConfiguration();
        }
        catch (Exception ex)
        {
            // Invalid user configuration should disable only the custom values, not the SPT server.
            logger.Warning("Could not read loading server config; defaults will be used: " + ex.Message);
            return new ServerLoadingConfiguration();
        }
    }

    private static void ScheduleReport()
    {
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Read(ref _lastReportTimestamp);
        if ((now - previous) * 1000d / Stopwatch.Frequency < MinimumReportIntervalMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastReportTimestamp, now, previous) != previous)
        {
            return;
        }

        _ = Task.Run(() => WriteReport("requests"));
    }

    private static void WriteReport(string reason)
    {
        if (!_configuration.WriteReports)
        {
            return;
        }

        lock (ReportLock)
        {
            try
            {
                string reportRoot = Path.Combine(ModRoot, "reports");
                Directory.CreateDirectory(reportRoot);
                RequestSample[] samples = Requests.ToArray();
                Process process = Process.GetCurrentProcess();
                StringBuilder text = new(8192);
                text.AppendLine("Tarkov Performance Suite - SPT Server Loading Report");
                text.AppendLine("Version: 1.0.1");
                text.AppendLine("Reason: " + reason);
                text.AppendLine("Observed uptime: " + Lifetime.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                text.AppendLine("Database ready: " + _databaseReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                text.AppendLine("Server ready: " + _serverReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                text.AppendLine(
                    "Process CPU consumed: " + process.TotalProcessorTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s"
                );
                text.AppendLine("Working set: " + (process.WorkingSet64 / 1048576d).ToString("F1", CultureInfo.InvariantCulture) + " MB");
                text.AppendLine("Fast response compression: " + _configuration.FastCompression);
                text.AppendLine("Requests captured: " + samples.Length);
                text.AppendLine();
                text.AppendLine("Top endpoints by total server time:");
                AppendEndpointSummary(text, samples);
                File.WriteAllText(Path.Combine(reportRoot, "server-loading-latest.txt"), text.ToString());
            }
            catch
            {
                // Reporting is diagnostic-only and runs during shutdown as well as normal operation. There may be
                // no usable logger or filesystem at that point, so a failed report must never stop the server.
            }
        }
    }

    private static void AppendEndpointSummary(StringBuilder text, RequestSample[] samples)
    {
        foreach (
            var group in samples
                .GroupBy(sample => sample.Method + " " + sample.Path)
                .Select(group => new
                {
                    Name = group.Key,
                    Total = group.Sum(sample => sample.ElapsedMs),
                    Max = group.Max(sample => sample.ElapsedMs),
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Total)
                .Take(50)
        )
        {
            text.AppendLine(
                group.Total.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10)
                    + " ms total | "
                    + group.Max.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9)
                    + " ms max | "
                    + group.Count.ToString(CultureInfo.InvariantCulture).PadLeft(4)
                    + " calls | "
                    + group.Name
            );
        }
    }
}
