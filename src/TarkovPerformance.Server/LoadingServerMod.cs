using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers.Http;

namespace TarkovPerformanceSuite.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.lucaswilluweit.tarkovperformancesuite.loadingserver";
    public override string Name { get; init; } = "Tarkov Performance Suite - Loading Server";
    public override string Author { get; init; } = "Lucas Willuweit";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 1)]
public sealed class LoadingServerBootstrap(ISptLogger<LoadingServerBootstrap> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.Initialize(logger);
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2)]
public sealed class DatabaseReadyProbe(ISptLogger<DatabaseReadyProbe> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.MarkDatabaseReady(logger);
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 2)]
public sealed class ServerReadyProbe(ISptLogger<ServerReadyProbe> logger) : IOnLoad
{
    public Task OnLoad()
    {
        ServerLoadingRuntime.MarkServerReady(logger);
        return Task.CompletedTask;
    }
}

internal static class ServerLoadingRuntime
{
    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();
    private static readonly ConcurrentQueue<RequestSample> Requests = new();
    private static readonly string ModRoot = Path.GetDirectoryName(typeof(ServerLoadingRuntime).Assembly.Location) ?? AppContext.BaseDirectory;
    private static readonly object ReportLock = new();
    private static ServerLoadingConfiguration _configuration = new();
    private static Harmony? _harmony;
    private static double _databaseReadyMs;
    private static double _serverReadyMs;
    private static long _lastReportTimestamp;
    private static int _requestCount;

    internal static bool FastCompressionEnabled => _configuration.FastCompression;
    internal static bool ReportsEnabled => _configuration.WriteReports;

    internal static void Initialize(ISptLogger<LoadingServerBootstrap> logger)
    {
        _configuration = LoadConfiguration(logger);
        ThreadPool.GetMinThreads(out int originalWorkers, out int originalIo);
        ThreadPool.GetMaxThreads(out int maxWorkers, out _);
        int desiredWorkers = Math.Min(maxWorkers, Math.Max(originalWorkers, Environment.ProcessorCount * Math.Clamp(_configuration.WorkerThreadsPerLogicalCpu, 1, 4)));
        bool threadPoolChanged = ThreadPool.SetMinThreads(desiredWorkers, originalIo);

        if (_configuration.RaiseProcessPriority)
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; }
            catch (Exception ex) { logger.Warning("Could not set above-normal server priority: " + ex.Message); }
        }

        _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.loadingserver");
        if (_configuration.WriteReports) PatchRequestTiming();
        if (_configuration.FastCompression) PatchFastCompression();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteReport("server-shutdown");

        logger.Info("Loading accelerator active: worker floor " + originalWorkers + " -> " + desiredWorkers
            + " (applied=" + threadPoolChanged + "), fast zlib=" + _configuration.FastCompression + ".");
    }

    internal static void MarkDatabaseReady<T>(ISptLogger<T> logger)
    {
        if (!_configuration.WriteReports) return;
        _databaseReadyMs = Lifetime.Elapsed.TotalMilliseconds;
        logger.Info("Loading accelerator: database-ready checkpoint at " + _databaseReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms.");
    }

    internal static void MarkServerReady<T>(ISptLogger<T> logger)
    {
        if (!_configuration.WriteReports) return;
        _serverReadyMs = Lifetime.Elapsed.TotalMilliseconds;
        logger.Info("Loading accelerator: server-ready checkpoint at " + _serverReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms.");
        WriteReport("server-ready");
    }

    internal static void RecordRequest(string method, string path, long started, Exception? error)
    {
        double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        Requests.Enqueue(new RequestSample(method, path, elapsedMs, error?.GetType().Name));
        int count = Interlocked.Increment(ref _requestCount);
        while (count > 4096 && Requests.TryDequeue(out _)) count = Interlocked.Decrement(ref _requestCount);

        bool important = elapsedMs >= _configuration.SlowRequestThresholdMs
            || path.Contains("/profile/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/location/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/fika/raid/", StringComparison.OrdinalIgnoreCase);
        if (important) ScheduleReport();
    }

    private static void PatchRequestTiming()
    {
        MethodInfo target = AccessTools.Method(typeof(SptHttpListener), nameof(SptHttpListener.Handle));
        _harmony!.Patch(target,
            prefix: new HarmonyMethod(typeof(ServerRequestPatches), nameof(ServerRequestPatches.HandlePrefix)),
            postfix: new HarmonyMethod(typeof(ServerRequestPatches), nameof(ServerRequestPatches.HandlePostfix)));
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
            if (!File.Exists(path)) return new ServerLoadingConfiguration();
            return JsonSerializer.Deserialize<ServerLoadingConfiguration>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? new ServerLoadingConfiguration();
        }
        catch (Exception ex)
        {
            logger.Warning("Could not read loading server config; defaults will be used: " + ex.Message);
            return new ServerLoadingConfiguration();
        }
    }

    private static void ScheduleReport()
    {
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Read(ref _lastReportTimestamp);
        if ((now - previous) * 1000d / Stopwatch.Frequency < 10_000d) return;
        if (Interlocked.CompareExchange(ref _lastReportTimestamp, now, previous) != previous) return;
        _ = Task.Run(() => WriteReport("requests"));
    }

    private static void WriteReport(string reason)
    {
        if (!_configuration.WriteReports) return;
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
                text.AppendLine("Version: 1.0.0");
                text.AppendLine("Reason: " + reason);
                text.AppendLine("Observed uptime: " + Lifetime.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                text.AppendLine("Database ready: " + _databaseReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                text.AppendLine("Server ready: " + _serverReadyMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
                text.AppendLine("Process CPU consumed: " + process.TotalProcessorTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                text.AppendLine("Working set: " + (process.WorkingSet64 / 1048576d).ToString("F1", CultureInfo.InvariantCulture) + " MB");
                text.AppendLine("Fast response compression: " + _configuration.FastCompression);
                text.AppendLine("Requests captured: " + samples.Length);
                text.AppendLine();
                text.AppendLine("Top endpoints by total server time:");
                foreach (var group in samples.GroupBy(sample => sample.Method + " " + sample.Path)
                    .Select(group => new { Name = group.Key, Total = group.Sum(sample => sample.ElapsedMs), Max = group.Max(sample => sample.ElapsedMs), Count = group.Count() })
                    .OrderByDescending(item => item.Total).Take(50))
                {
                    text.AppendLine(group.Total.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10) + " ms total | "
                        + group.Max.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) + " ms max | "
                        + group.Count.ToString(CultureInfo.InvariantCulture).PadLeft(4) + " calls | " + group.Name);
                }
                string path = Path.Combine(reportRoot, "server-loading-latest.txt");
                File.WriteAllText(path, text.ToString());
            }
            catch { }
        }
    }

    internal readonly record struct RequestSample(string Method, string Path, double ElapsedMs, string? Error);
}

internal sealed class ServerLoadingConfiguration
{
    public int WorkerThreadsPerLogicalCpu { get; set; } = 2;
    public bool RaiseProcessPriority { get; set; } = true;
    public bool FastCompression { get; set; } = true;
    public bool WriteReports { get; set; } = false;
    public int SlowRequestThresholdMs { get; set; } = 250;
}

public static class ServerRequestPatches
{
    public static void HandlePrefix(HttpContext context, out ServerRequestState __state)
    {
        __state = new ServerRequestState(Stopwatch.GetTimestamp(), context.Request.Method, context.Request.Path.ToString());
    }

    public static void HandlePostfix(ServerRequestState __state, ref Task __result)
    {
        __result = AwaitAndRecord(__result, __state);
    }

    public static bool SendZlibJsonPrefix(HttpResponse resp, string output, MongoId sessionID, ref Task __result)
    {
        if (!ServerLoadingRuntime.FastCompressionEnabled) return true;
        __result = SendFastZlib(resp, output, sessionID);
        return false;
    }

    private static async Task AwaitAndRecord(Task original, ServerRequestState state)
    {
        Exception? error = null;
        try { await original.ConfigureAwait(false); }
        catch (Exception ex) { error = ex; throw; }
        finally { ServerLoadingRuntime.RecordRequest(state.Method, state.Path, state.Started, error); }
    }

    private static async Task SendFastZlib(HttpResponse response, string output, MongoId sessionId)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/json";
        response.Headers.Append("Set-Cookie", "PHPSESSID=" + sessionId);
        await using ZLibStream stream = new(response.Body, CompressionLevel.Fastest, leaveOpen: true);
        byte[] bytes = Encoding.UTF8.GetBytes(output);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }
}

public readonly record struct ServerRequestState(long Started, string Method, string Path);
