namespace TarkovPerformanceSuite.RuntimeFeatures
{
    // Retained only so benchmark readers keep a stable CSV/JSON schema. Version 0.5
    // no longer forces Unity LOD groups after real-world reports of missing objects.
    internal readonly struct RemoteRenderLodCounters
    {
        internal int RegisteredAi => 0;
        internal int MidTierAi => 0;
        internal int FarTierAi => 0;
        internal int ForcedLodGroups => 0;
        internal int ModifiedSkinnedRenderers => 0;
        internal int ModifiedRenderers => 0;
        internal double AverageMs => 0;
    }
}
