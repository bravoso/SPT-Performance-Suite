namespace TarkovPerformanceSuite.RuntimeFeatures;

// Retained only so benchmark readers keep a stable CSV/JSON schema. Version 0.5
// no longer forces Unity LOD groups after real-world reports of missing objects.
/// <summary>Reserved counters for the retired remote render-LOD experiment.</summary>
internal readonly struct RemoteRenderLodCounters
{
    internal int RegisteredAi
    {
        get { return 0; }
    }

    internal int MidTierAi
    {
        get { return 0; }
    }

    internal int FarTierAi
    {
        get { return 0; }
    }

    internal int ForcedLodGroups
    {
        get { return 0; }
    }

    internal int ModifiedSkinnedRenderers
    {
        get { return 0; }
    }

    internal int ModifiedRenderers
    {
        get { return 0; }
    }

    internal double AverageMs
    {
        get { return 0; }
    }
}
