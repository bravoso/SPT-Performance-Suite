using System;
using System.IO;
using System.Reflection;
using TarkovPerformanceSuite.Diagnostics;
using TarkovPerformanceSuite.Features;
using TarkovPerformanceSuite.Utilities;

namespace TarkovPerformanceSuite.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("rolling frame statistics", TestFrameStatistics);
            Run("percentile interpolation", TestPercentiles);
            Run("benchmark CSV and JSON serialization", TestSerialization);
            Run("entity classification", TestEntityClassification);
            Run("configuration validation", TestConfiguration);
            Run("adaptive distance hysteresis", TestAdaptiveDistance);
            Run("circuit breaker", TestCircuitBreaker);
            Run("time scheduler", TestScheduler);
            Run("stable frame work budget", TestFrameWorkBudget);
            Run("original-state restore", TestStateCache);
            Run("version comparison", TestVersions);
            Run("method signature fingerprint", TestFingerprint);
            Console.WriteLine($"Passed: {_passed}; Failed: {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        private static void TestFrameStatistics()
        {
            var stats = new FrameStatistics(4);
            stats.Add(10); stats.Add(20); stats.Add(30); stats.Add(40); stats.Add(50);
            FrameStatisticsSnapshot snapshot = stats.Snapshot();
            Equal(4, snapshot.Count);
            Near(35, snapshot.AverageMs);
            Near(35, snapshot.MedianMs);
            Near(20, snapshot.MinimumFps);
        }

        private static void TestPercentiles() => Near(25, Percentiles.FromSorted(new[] { 10d, 20d, 30d, 40d }, 4, 0.5));

        private static void TestSerialization()
        {
            var export = new BenchmarkExport
            {
                StartedUtc = "2026-01-01T00:00:00Z",
                MapName = "factory4_day",
                EnabledFeatures = "shadow=false",
                Samples = new[] { new BenchmarkSample { TimestampSeconds = 1, FrameTimeMs = 10, Fps = 100, MainThreadMs = 7, GpuFrameMs = 4, WaitForTargetFpsMs = 2, PlayerCount = 2, AiCount = 1, ShadowEffectiveDistance = 75, ShadowDisabledRendererCount = 20, SkinningModifiedRendererCount = 4, RemoteLodFarAiCount = 3, RemoteLodForcedGroupCount = 8, DeclutterHiddenRendererCount = 120, RemoteBudgetedCharacterCount = 6, RemoteSkippedPropUpdates = 42, CompatibilityFastWorldLookups = 8 } }
            };
            var csv = new StringWriter(); BenchmarkSerializer.WriteCsv(csv, export);
            var json = new StringWriter(); BenchmarkSerializer.WriteJson(json, export);
            True(csv.ToString().Contains("frame_time_ms"));
            True(csv.ToString().Contains("gpu_frame_ms"));
            True(csv.ToString().Contains("shadow_effective_distance"));
            True(csv.ToString().Contains("remote_lod_forced_group_count"));
            True(csv.ToString().Contains(",120,"));
            True(csv.ToString().Contains("shadow=false"));
            True(json.ToString().Contains("factory4_day"));
            True(json.ToString().Contains("\"frameTimeMs\":10"));
            True(json.ToString().Contains("\"gpuFrameMs\":4"));
            True(json.ToString().Contains("\"shadowEffectiveDistance\":75"));
            True(json.ToString().Contains("\"remoteLodForcedGroupCount\":8"));
            True(json.ToString().Contains("\"declutterHiddenRendererCount\":120"));
            True(json.ToString().Contains("\"remoteBudgetedCharacterCount\":6"));
            True(json.ToString().Contains("\"remoteSkippedPropUpdates\":42"));
            True(json.ToString().Contains("\"compatibilityFastWorldLookups\":8"));
        }

        private static void TestEntityClassification()
        {
            Equal(EntityKind.LocalPlayer, EntityClassifierLogic.Classify(new EntitySignals(true, true, false, false, null, false)));
            Equal(EntityKind.RemoteAI, EntityClassifierLogic.Classify(new EntitySignals(true, false, true, false, null, false)));
            Equal(EntityKind.RemoteAI, EntityClassifierLogic.Classify(new EntitySignals(true, false, false, true, true, false)));
            Equal(EntityKind.RemoteHuman, EntityClassifierLogic.Classify(new EntitySignals(true, false, false, true, false, false)));
            Equal(EntityKind.UnknownPlayerLikeEntity, EntityClassifierLogic.Classify(new EntitySignals(true, false, false, false, null, false)));
        }

        private static void TestConfiguration()
        {
            ValidatedConfiguration value = ConfigurationValidator.Validate(1, 5000, 0.01, 5000, 500, 1, 0.001, 50);
            Near(5, value.CaptureSeconds); Near(1000, value.ShadowDistance); Near(0.1, value.UpdateIntervalSeconds);
            Near(1000, value.ShadowMinimumDistance); Near(240, value.ShadowTargetFps);
            Near(20, value.SkinningDistance); Near(0.05, value.SkinningUpdateIntervalSeconds); Near(10, value.SkinningOffscreenHoldSeconds);
        }

        private static void TestAdaptiveDistance()
        {
            var controller = new AdaptiveDistanceController();
            controller.Reset(120);
            controller.Update(1.0 / 60.0, 100, 120, 60, 60);
            for (int i = 0; i < 120; i++) controller.Update(1.0 / 60.0, 12, 120, 60, 60);
            Near(120, controller.EffectiveDistance);
            for (int i = 0; i < 240; i++) controller.Update(1.0 / 60.0, 25, 120, 60, 60);
            True(controller.EffectiveDistance < 120);
            True(controller.EffectiveDistance >= 60);
            for (int i = 0; i < 3600; i++) controller.Update(1.0 / 120.0, 8, 120, 60, 60);
            Near(120, controller.EffectiveDistance);
        }

        private static void TestCircuitBreaker()
        {
            var breaker = new CircuitBreaker(3);
            True(!breaker.Failure()); True(!breaker.Failure()); True(breaker.Failure()); True(breaker.IsOpen);
            breaker.Reset(); True(!breaker.IsOpen);
        }

        private static void TestScheduler()
        {
            var scheduler = new TimeScheduler(0.25);
            True(scheduler.IsDue(0)); True(!scheduler.IsDue(0.1)); True(scheduler.IsDue(0.25));
        }

        private static void TestFrameWorkBudget()
        {
            int runs = 0;
            for (int frame = 0; frame < 16; frame++) if (FrameWorkBudget.ShouldRun(frame, 7, 4)) runs++;
            Equal(4, runs);
            True(FrameWorkBudget.ShouldRun(3, 9, 1));
        }

        private static void TestStateCache()
        {
            var cache = new OriginalStateCache<string, int>();
            int restored = 0;
            True(cache.Remember("renderer", 2)); True(!cache.Remember("renderer", 3));
            cache.RestoreAll((_, state) => restored = state);
            Equal(2, restored); Equal(0, cache.Count);
        }

        private static void TestVersions()
        {
            True(VersionTools.Compare("4.0.13+hash", "4.0.12") > 0);
            Equal(new Version(2, 3, 3), VersionTools.Parse("2.3.3-beta"));
        }

        private static void TestFingerprint()
        {
            MethodInfo method = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) });
            string text = MethodSignatureFingerprint.Describe(method);
            True(text.Contains("System.String::StartsWith(System.String)->System.Boolean"));
            Equal(64, MethodSignatureFingerprint.Sha256(method).Length);
        }

        private static void Run(string name, Action test)
        {
            try { test(); _passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true"); }
        private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}"); }
        private static void Near(double expected, double actual) { if (Math.Abs(expected - actual) > 0.0001) throw new InvalidOperationException($"Expected {expected}, got {actual}"); }
    }
}
