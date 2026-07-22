namespace TarkovPerformanceSuite.Server.Diagnostics;

/// <summary>
/// Carries request timing data from a Harmony prefix to its asynchronous postfix.
/// </summary>
public readonly record struct ServerRequestState(long Started, string Method, string Path);

/// <summary>
/// Immutable request measurement retained by the bounded in-memory report queue.
/// </summary>
internal readonly record struct RequestSample(string Method, string Path, double ElapsedMs, string? Error);
