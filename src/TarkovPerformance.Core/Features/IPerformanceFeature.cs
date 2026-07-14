namespace TarkovPerformanceSuite.Features
{
    public interface IPerformanceFeature
    {
        string Name { get; }
        bool IsAvailable { get; }
        bool IsEnabled { get; }
        void Initialize();
        void OnRaidStarted();
        void OnRaidEnded();
        void SetEnabled(bool enabled);
        void Shutdown();
    }
}

