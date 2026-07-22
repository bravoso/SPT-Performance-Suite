namespace TarkovPerformanceSuite.Server.Configuration;

/// <summary>
/// Persistent settings for the optional SPT server loading component.
/// </summary>
internal sealed class ServerLoadingConfiguration
{
    public int WorkerThreadsPerLogicalCpu { get; set; } = 2;
    public bool RaiseProcessPriority { get; set; } = true;
    public bool FastCompression { get; set; } = true;
    public bool WriteReports { get; set; }
    public int SlowRequestThresholdMs { get; set; } = 250;
}
