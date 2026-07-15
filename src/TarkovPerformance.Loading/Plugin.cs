using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Common.Http;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProcessPriorityClass = System.Diagnostics.ProcessPriorityClass;
using SystemThreadPriority = System.Threading.ThreadPriority;
using UnityThreadPriority = UnityEngine.ThreadPriority;

namespace TarkovPerformanceSuite.Loading
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("EscapeFromTarkov.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lucaswilluweit.tarkovperformancesuite.loading";
        public const string PluginName = "Tarkov Performance Suite - Loading Accelerator";
        public const string PluginVersion = "1.0.0";

        private readonly Stopwatch _lifetime = Stopwatch.StartNew();
        private readonly ConcurrentQueue<RequestSample> _requests = new ConcurrentQueue<RequestSample>();
        private readonly object _stageGate = new object();
        private readonly List<LoadingStageSample> _loadingStages = new List<LoadingStageSample>();
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<bool> _optimizeSyncRequests;
        private ConfigEntry<bool> _writeReports;
        private ConfigEntry<bool> _raiseProcessPriority;
        private ConfigEntry<bool> _raiseMainThreadPriority;
        private ConfigEntry<int> _uploadTimeSlice;
        private ConfigEntry<int> _uploadBufferMb;
        private ConfigEntry<int> _workerThreadsPerCore;
        private ConfigEntry<bool> _useAllUnityJobWorkers;
        private ConfigEntry<bool> _concurrentSceneBundlePreload;
        private ConfigEntry<int> _sceneBundlePreloadConcurrency;
        private ConfigEntry<bool> _parallelResourceClassification;
        private ConfigEntry<bool> _parallelLootSerialization;
        private ConfigEntry<bool> _linearStaticLootMatching;
        private ConfigEntry<int> _resourceClassificationWorkers;
        private ConfigEntry<int> _resourceClassificationThreshold;
        private ConfigEntry<int> _slowRequestMs;
        private LoadingSnapshot _original;
        private bool _haveOriginal;
        private bool _boostApplied;
        private LoadingPhase _phase = LoadingPhase.StartupOrMenu;
        private float _nextStateCheck;
        private float _postRaidReportAt;
        private int _requestCount;
        private string _outputRoot;
        private bool _stagePatchesInstalled;
        private bool _optimizationPatchesInstalled;
        private float _nextStagePatchAttempt;
        private long _parallelClassificationTicks;
        private int _parallelClassificationItems;
        private int _parallelClassificationPasses;
        private int _sceneBundlePreloadPasses;
        private int _sceneBundlesPreloaded;
        private long _sceneBundlePreloadTicks;
        private long _parallelLootSerializationTicks;
        private int _parallelLootItems;
        private int _parallelLootPasses;
        private long _staticLootMatchingTicks;
        private int _staticLootMatchingEntries;
        private int _staticLootMatchingPasses;

        private static Plugin Instance { get; set; }

        private void Awake()
        {
            Instance = this;
            BindConfiguration();
            _outputRoot = Path.Combine(BepInEx.Paths.PluginPath, "TarkovPerformanceSuite", "loading-reports");
            _original = LoadingSnapshot.Capture();
            _haveOriginal = true;

            if (!_enabled.Value)
            {
                Logger.LogInfo(PluginName + " is disabled in F12 settings.");
                return;
            }

            ApplyLoadingBoost();
            if (_optimizeSyncRequests.Value) PatchSynchronousRequests();
            TryPatchLoadingOptimizations();
            if (_writeReports.Value) TryPatchLoadingStages();
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Loading boost is active until the local player is ready.");
        }

        private void Update()
        {
            if (!_enabled.Value)
            {
                RestoreLoadingSettings();
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_writeReports.Value && !_stagePatchesInstalled && now >= _nextStagePatchAttempt)
            {
                _nextStagePatchAttempt = now + 2f;
                TryPatchLoadingStages();
            }
            if (now < _nextStateCheck) return;
            _nextStateCheck = now + 0.25f;

            GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            LoadingPhase detected = world == null
                ? LoadingPhase.StartupOrMenu
                : HasLocalPlayer(world) ? LoadingPhase.InRaid : LoadingPhase.RaidLoading;

            if (detected != _phase)
            {
                LoadingPhase previous = _phase;
                _phase = detected;
                Logger.LogInfo("Loading phase: " + previous + " -> " + detected + " at " + _lifetime.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");

                if (detected == LoadingPhase.InRaid)
                {
                    FinishLoadingStage();
                    RestoreLoadingSettings();
                    WriteReport("raid-ready");
                }
                else
                {
                    ApplyLoadingBoost();
                    if (previous == LoadingPhase.InRaid) _postRaidReportAt = now + 8f;
                }
            }

            if (_postRaidReportAt > 0f && now >= _postRaidReportAt)
            {
                _postRaidReportAt = 0f;
                WriteReport("post-raid-menu");
            }
        }

        private void OnDestroy()
        {
            try { WriteReport("shutdown"); }
            catch { }
            try { _harmony?.UnpatchSelf(); }
            catch { }
            DedicatedRequestWorker.Shutdown();
            RestoreLoadingSettings();
            Instance = null;
        }

        private void BindConfiguration()
        {
            const string section = "Loading accelerator";
            _enabled = Config.Bind(section, "Enabled", true, "Boost game/profile/raid loading. Gameplay settings are restored when your player is ready.");
            _optimizeSyncRequests = Config.Bind(section, "Remove redundant request worker hop", true, "Uses reusable background request workers so Unity cannot deadlock while waiting for synchronous SPT requests.");
            _writeReports = Config.Bind(section, "Write loading reports", false, "Writes request and phase timings under TarkovPerformanceSuite/loading-reports. Disabled by default for normal play.");
            _raiseProcessPriority = Config.Bind(section, "Above-normal process priority while loading", true, "Gives loading work preference, then restores the previous process priority in raid.");
            _raiseMainThreadPriority = Config.Bind(section, "Above-normal main thread while loading", true, "Prioritizes Unity integration work during loading only.");
            _uploadTimeSlice = Config.Bind(section, "Asset upload time per frame ms", 16, new ConfigDescription("Unity main-thread time allowed for asset uploads while loading. EFT doubles this to 32 ms during raid scene loading, which is Unity's supported maximum.", new AcceptableValueRange<int>(2, 16)));
            _uploadBufferMb = Config.Bind(section, "Asset upload buffer MB", 256, new ConfigDescription("System-memory staging buffer used while loading textures and meshes. EFT temporarily doubles this during raid scene loading.", new AcceptableValueRange<int>(32, 256)));
            _workerThreadsPerCore = Config.Bind(section, "Minimum workers per logical CPU", 2, new ConfigDescription("Prevents the worker pool from slowly ramping up during loading bursts.", new AcceptableValueRange<int>(1, 4)));
            _useAllUnityJobWorkers = Config.Bind(section, "Use all Unity job workers while loading", true, "Lets Unity use its hardware-specific maximum worker count for loading jobs, then restores the previous count in raid.");
            _concurrentSceneBundlePreload = Config.Bind(section, "Preload raid scene bundles concurrently", true, "Loads scene bundle files in small concurrent batches, then lets EFT integrate and activate every scene in its original order.");
            _sceneBundlePreloadConcurrency = Config.Bind(section, "Scene bundle preload concurrency", 4, new ConfigDescription("Maximum scene bundle files prepared at once. Scene creation and activation remain single-file and ordered.", new AcceptableValueRange<int>(2, 8)));
            _parallelResourceClassification = Config.Bind(section, "Parallel item and bundle preparation", true, "Uses several CPU threads for the read-only item-to-bundle classification pass before pools are created.");
            _parallelLootSerialization = Config.Bind(section, "Parallel headless loot preparation", true, "Splits Fika host/headless read-only loot serialization across CPU threads before it is sent to clients.");
            _linearStaticLootMatching = Config.Bind(section, "Fast static loot and container matching", true, "Replaces EFT's containers-times-loot nested scan with a single ID lookup pass while loading the map.");
            _resourceClassificationWorkers = Config.Bind(section, "Item preparation worker limit", 0, new ConfigDescription("0 uses all logical CPUs. Lower this if loading competes with another SPT instance on the same PC.", new AcceptableValueRange<int>(0, 64)));
            _resourceClassificationThreshold = Config.Bind(section, "Parallel item preparation minimum count", 128, new ConfigDescription("Small lists stay single-threaded to avoid parallel scheduling overhead.", new AcceptableValueRange<int>(32, 2048)));
            _slowRequestMs = Config.Bind(section, "Slow request warning ms", 250, new ConfigDescription("Requests slower than this are highlighted in the log/report.", new AcceptableValueRange<int>(50, 5000)));
        }

        private void ApplyLoadingBoost()
        {
            if (_boostApplied) return;
            try
            {
                QualitySettings.asyncUploadTimeSlice = _uploadTimeSlice.Value;
                QualitySettings.asyncUploadBufferSize = _uploadBufferMb.Value;
                QualitySettings.asyncUploadPersistentBuffer = true;
                Application.backgroundLoadingPriority = UnityThreadPriority.High;

                ThreadPool.GetMaxThreads(out int maxWorkers, out _);
                int desiredWorkers = Math.Min(maxWorkers, Math.Max(_original.MinWorkerThreads, Environment.ProcessorCount * _workerThreadsPerCore.Value));
                ThreadPool.SetMinThreads(desiredWorkers, _original.MinIoThreads);

                if (_useAllUnityJobWorkers.Value)
                {
                    int maximum = JobsUtility.JobWorkerMaximumCount;
                    if (maximum > 0 && JobsUtility.JobWorkerCount != maximum)
                        JobsUtility.JobWorkerCount = maximum;
                }

                if (_raiseMainThreadPriority.Value) Thread.CurrentThread.Priority = SystemThreadPriority.AboveNormal;
                if (_raiseProcessPriority.Value)
                {
                    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; }
                    catch (Exception ex) { Logger.LogDebug("Could not change process priority: " + ex.Message); }
                }
                _boostApplied = true;
                Logger.LogInfo("Loading boost applied: upload " + QualitySettings.asyncUploadTimeSlice + " ms/"
                    + QualitySettings.asyncUploadBufferSize + " MB, managed workers >= " + desiredWorkers
                    + ", Unity jobs " + JobsUtility.JobWorkerCount + "/" + JobsUtility.JobWorkerMaximumCount + ".");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Loading boost failed open: " + ex.Message);
            }
        }

        private void RestoreLoadingSettings()
        {
            if (!_boostApplied || !_haveOriginal) return;
            try
            {
                _original.Apply();
                _boostApplied = false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not fully restore loading settings: " + ex.Message);
            }
        }

        private void PatchSynchronousRequests()
        {
            DedicatedRequestWorker.Initialize(Math.Min(4, Math.Max(2, Environment.ProcessorCount / 4)));
            if (_harmony == null) _harmony = new Harmony(PluginGuid);
            Patch(nameof(RequestHandler.GetData), nameof(RequestPatches.GetDataPrefix));
            Patch(nameof(RequestHandler.GetJson), nameof(RequestPatches.GetJsonPrefix));
            Patch(nameof(RequestHandler.PostJson), nameof(RequestPatches.PostJsonPrefix));
            Patch(nameof(RequestHandler.PutJson), nameof(RequestPatches.PutJsonPrefix));
        }

        private void TryPatchLoadingOptimizations()
        {
            if (_optimizationPatchesInstalled) return;
            try
            {
                if (_harmony == null) _harmony = new Harmony(PluginGuid);

                MethodInfo sceneListLoader = AccessTools.Method(typeof(GClass2287), "method_2");
                MethodInfo classifyResources = AccessTools.Method(typeof(PoolManagerClass.Class1448), "ConvertResourceInfo");
                MethodInfo serializeLoot = AccessTools.Method(typeof(EFTItemSerializerClass), "SerializeLootData");
                MethodInfo matchStaticLoot = AccessTools.Method(typeof(GameWorld), "method_7");
                if (sceneListLoader == null || classifyResources == null || serializeLoot == null || matchStaticLoot == null)
                    throw new MissingMethodException("The inspected EFT loading methods were not found.");

                _harmony.Patch(sceneListLoader,
                    prefix: new HarmonyMethod(typeof(LoadingOptimizationPatches), nameof(LoadingOptimizationPatches.ConcurrentSceneBundlePreloadPrefix)));
                _harmony.Patch(classifyResources,
                    prefix: new HarmonyMethod(typeof(LoadingOptimizationPatches), nameof(LoadingOptimizationPatches.ParallelResourceClassificationPrefix)));
                _harmony.Patch(serializeLoot,
                    prefix: new HarmonyMethod(typeof(LoadingOptimizationPatches), nameof(LoadingOptimizationPatches.ParallelLootSerializationPrefix)));
                _harmony.Patch(matchStaticLoot,
                    prefix: new HarmonyMethod(typeof(LoadingOptimizationPatches), nameof(LoadingOptimizationPatches.FastStaticLootMatchingPrefix)));
                _optimizationPatchesInstalled = true;
                Logger.LogInfo("Loading parallelism attached (concurrent scene-bundle preload + read-only item/bundle and headless loot preparation). Scene integration remains ordered.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Loading parallelism was not attached; vanilla loading remains active: " + ex.Message);
            }
        }

        private void TryPatchLoadingStages()
        {
            if (_stagePatchesInstalled) return;
            try
            {
                Assembly fika = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, "Fika.Core", StringComparison.OrdinalIgnoreCase));
                Type coopGame = fika?.GetType("Fika.Core.Main.GameMode.CoopGame", false);
                Type mapLoader = typeof(EFT.TarkovApplication).GetNestedType("Class1505", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo lootProgress = coopGame == null ? null : AccessTools.Method(coopGame, "method_20");
                MethodInfo mapLoading = mapLoader == null ? null : AccessTools.Method(mapLoader, "method_1");
                MethodInfo mapCaching = mapLoader == null ? null : AccessTools.Method(mapLoader, "method_2");
                if (lootProgress == null || mapLoading == null || mapCaching == null) return;

                if (_harmony == null) _harmony = new Harmony(PluginGuid);
                _harmony.Patch(lootProgress, postfix: new HarmonyMethod(typeof(LoadingStagePatches), nameof(LoadingStagePatches.LootProgressPostfix)));
                _harmony.Patch(mapLoading, postfix: new HarmonyMethod(typeof(LoadingStagePatches), nameof(LoadingStagePatches.MapLoadingPostfix)));
                _harmony.Patch(mapCaching, postfix: new HarmonyMethod(typeof(LoadingStagePatches), nameof(LoadingStagePatches.MapCachingPostfix)));
                _stagePatchesInstalled = true;
                Logger.LogInfo("Detailed Fika loading-stage profiler attached (map loading, caching, bundles and loot).");
            }
            catch (Exception ex)
            {
                Logger.LogDebug("Detailed loading-stage profiler is not ready: " + ex.Message);
            }
        }

        internal static void RecordLoadingProgress(string stage, float progress)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            plugin.RecordStage(stage, progress);
        }

        internal static bool ShouldPreloadSceneBundles(int sceneCount)
            => Instance != null && Instance._boostApplied && Instance._concurrentSceneBundlePreload != null
                && Instance._concurrentSceneBundlePreload.Value && sceneCount > 1;

        internal static int SceneBundlePreloadConcurrency
            => Instance == null ? 2 : Math.Max(2, Math.Min(8, Instance._sceneBundlePreloadConcurrency.Value));

        internal static bool ShouldParallelizeResourceClassification(int itemCount)
            => Instance != null && Instance._boostApplied && Instance._parallelResourceClassification != null
                && Instance._parallelResourceClassification.Value && itemCount >= Instance._resourceClassificationThreshold.Value;

        internal static int ResourceClassificationWorkers
        {
            get
            {
                Plugin plugin = Instance;
                if (plugin == null) return 1;
                int configured = plugin._resourceClassificationWorkers.Value;
                return Math.Max(1, Math.Min(Environment.ProcessorCount, configured <= 0 ? Environment.ProcessorCount : configured));
            }
        }

        internal static bool ShouldParallelizeLootSerialization(int itemCount)
            => Instance != null && Instance._boostApplied && Instance._parallelLootSerialization != null
                && Instance._parallelLootSerialization.Value && itemCount >= Instance._resourceClassificationThreshold.Value;

        internal static bool ShouldUseFastStaticLootMatching
            => Instance != null && Instance._boostApplied && Instance._linearStaticLootMatching != null
                && Instance._linearStaticLootMatching.Value;

        internal static void RecordSceneBundlePreload(int bundles, long elapsedTicks)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            Interlocked.Increment(ref plugin._sceneBundlePreloadPasses);
            Interlocked.Add(ref plugin._sceneBundlesPreloaded, bundles);
            Interlocked.Add(ref plugin._sceneBundlePreloadTicks, elapsedTicks);
        }

        internal static void RecordParallelClassification(int items, long elapsedTicks)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            Interlocked.Add(ref plugin._parallelClassificationItems, items);
            Interlocked.Add(ref plugin._parallelClassificationTicks, elapsedTicks);
            Interlocked.Increment(ref plugin._parallelClassificationPasses);
        }

        internal static void RecordParallelLootSerialization(int items, long elapsedTicks)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            Interlocked.Add(ref plugin._parallelLootItems, items);
            Interlocked.Add(ref plugin._parallelLootSerializationTicks, elapsedTicks);
            Interlocked.Increment(ref plugin._parallelLootPasses);
        }

        internal static void RecordStaticLootMatching(int entries, long elapsedTicks)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            Interlocked.Add(ref plugin._staticLootMatchingEntries, entries);
            Interlocked.Add(ref plugin._staticLootMatchingTicks, elapsedTicks);
            Interlocked.Increment(ref plugin._staticLootMatchingPasses);
        }

        private void RecordStage(string stage, float progress)
        {
            double now = _lifetime.Elapsed.TotalSeconds;
            lock (_stageGate)
            {
                LoadingStageSample current = _loadingStages.Count == 0 ? null : _loadingStages[_loadingStages.Count - 1];
                if (current == null || !string.Equals(current.Name, stage, StringComparison.Ordinal))
                {
                    if (current != null) current.EndSeconds = now;
                    current = new LoadingStageSample(stage, now);
                    _loadingStages.Add(current);
                    Logger.LogInfo("Detailed loading stage: " + stage + " started at " + now.ToString("F2", CultureInfo.InvariantCulture) + " s");
                }
                current.LastProgress = Math.Max(current.LastProgress, progress);
                current.EndSeconds = now;
            }
        }

        private void FinishLoadingStage()
        {
            lock (_stageGate)
            {
                if (_loadingStages.Count > 0) _loadingStages[_loadingStages.Count - 1].EndSeconds = _lifetime.Elapsed.TotalSeconds;
            }
        }

        private void Patch(string targetName, string prefixName)
        {
            MethodInfo target = typeof(RequestHandler).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == targetName && !method.Name.EndsWith("Async", StringComparison.Ordinal));
            MethodInfo prefix = typeof(RequestPatches).GetMethod(prefixName, BindingFlags.Public | BindingFlags.Static);
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        internal static long BeginRequest()
        {
            Plugin plugin = Instance;
            return plugin != null && plugin._writeReports != null && plugin._writeReports.Value ? Stopwatch.GetTimestamp() : 0L;
        }

        internal static void EndRequest(string method, string path, long started, Exception error = null)
        {
            Plugin plugin = Instance;
            if (plugin == null || started == 0L || plugin._writeReports == null || !plugin._writeReports.Value) return;
            double elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            plugin.RecordRequest(new RequestSample(plugin._phase, method, path ?? "(unknown)", elapsedMs, error?.GetType().Name));
        }

        private void RecordRequest(RequestSample sample)
        {
            _requests.Enqueue(sample);
            int count = Interlocked.Increment(ref _requestCount);
            while (count > 4096 && _requests.TryDequeue(out _)) count = Interlocked.Decrement(ref _requestCount);
            if (sample.ElapsedMs >= _slowRequestMs.Value)
                Logger.LogWarning("Slow loading request: " + sample.Method + " " + sample.Path + " took " + sample.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
        }

        private void WriteReport(string reason)
        {
            if (_writeReports == null || !_writeReports.Value) return;
            try
            {
                Directory.CreateDirectory(_outputRoot);
                RequestSample[] samples = _requests.ToArray();
                Process process = Process.GetCurrentProcess();
                StringBuilder text = new StringBuilder(8192);
                text.AppendLine("Tarkov Performance Suite - Loading Report");
                text.AppendLine("Version: " + PluginVersion);
                text.AppendLine("Reason: " + reason);
                text.AppendLine("Phase: " + _phase);
                text.AppendLine("Runtime role: " + (IsHeadlessProcess() ? "headless" : "client"));
                text.AppendLine("Process ID: " + process.Id);
                text.AppendLine("Process uptime observed: " + _lifetime.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                text.AppendLine("Process CPU consumed: " + process.TotalProcessorTime.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                text.AppendLine("Working set: " + (process.WorkingSet64 / 1048576d).ToString("F1", CultureInfo.InvariantCulture) + " MB");
                text.AppendLine("Managed memory: " + (GC.GetTotalMemory(false) / 1048576d).ToString("F1", CultureInfo.InvariantCulture) + " MB");
                text.AppendLine("Logical CPUs: " + Environment.ProcessorCount);
                text.AppendLine("Loading upload budget: " + _uploadTimeSlice.Value + " ms / " + _uploadBufferMb.Value + " MB (EFT doubles these during raid scene loading, capped by the 32 ms / 512 MB preset)");
                text.AppendLine("Unity job workers requested: " + (_useAllUnityJobWorkers.Value ? "maximum" : "unchanged"));
                text.AppendLine("Concurrent scene bundle preload: " + Volatile.Read(ref _sceneBundlePreloadPasses) + " passes, "
                    + Volatile.Read(ref _sceneBundlesPreloaded) + " bundles, "
                    + (Volatile.Read(ref _sceneBundlePreloadTicks) * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms wall time");
                text.AppendLine("Parallel item preparation: " + Volatile.Read(ref _parallelClassificationPasses) + " passes, "
                    + Volatile.Read(ref _parallelClassificationItems) + " items, "
                    + (Volatile.Read(ref _parallelClassificationTicks) * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms wall time");
                text.AppendLine("Parallel headless loot preparation: " + Volatile.Read(ref _parallelLootPasses) + " passes, "
                    + Volatile.Read(ref _parallelLootItems) + " top-level loot entries, "
                    + (Volatile.Read(ref _parallelLootSerializationTicks) * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms wall time");
                text.AppendLine("Fast static loot matching: " + Volatile.Read(ref _staticLootMatchingPasses) + " passes, "
                    + Volatile.Read(ref _staticLootMatchingEntries) + " total lookup entries, "
                    + (Volatile.Read(ref _staticLootMatchingTicks) * 1000.0 / Stopwatch.Frequency).ToString("F2", CultureInfo.InvariantCulture) + " ms wall time");
                text.AppendLine("Requests captured: " + samples.Length);
                text.AppendLine();
                text.AppendLine("Detailed local loading stages:");
                lock (_stageGate)
                {
                    if (_loadingStages.Count == 0) text.AppendLine("  unavailable (Fika stage hooks were not active)");
                    for (int i = 0; i < _loadingStages.Count; i++)
                    {
                        LoadingStageSample stage = _loadingStages[i];
                        text.AppendLine((stage.EndSeconds - stage.StartSeconds).ToString("F2", CultureInfo.InvariantCulture).PadLeft(8)
                            + " s | " + stage.Name + " | final progress " + (stage.LastProgress * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%");
                    }
                }
                text.AppendLine();
                text.AppendLine("Top server endpoints by total blocking time:");
                foreach (var group in samples.GroupBy(sample => sample.Method + " " + sample.Path)
                    .Select(group => new { Name = group.Key, Total = group.Sum(sample => sample.ElapsedMs), Max = group.Max(sample => sample.ElapsedMs), Count = group.Count() })
                    .OrderByDescending(item => item.Total).Take(40))
                {
                    text.AppendLine(group.Total.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10) + " ms total | "
                        + group.Max.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) + " ms max | "
                        + group.Count.ToString(CultureInfo.InvariantCulture).PadLeft(4) + " calls | " + group.Name);
                }
                text.AppendLine();
                text.AppendLine("Request timeline:");
                foreach (RequestSample sample in samples)
                    text.AppendLine(sample.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) + " ms | " + sample.Phase + " | " + sample.Method + " " + sample.Path + (sample.Error == null ? "" : " | ERROR " + sample.Error));

                string file = Path.Combine(_outputRoot, "loading-" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture)
                    + "-p" + process.Id.ToString(CultureInfo.InvariantCulture) + "-" + (IsHeadlessProcess() ? "headless" : "client") + "-" + reason + ".txt");
                File.WriteAllText(file, text.ToString());
                Logger.LogInfo("Loading report written: " + file);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not write loading report: " + ex.Message);
            }
        }

        private static bool HasLocalPlayer(GameWorld world)
        {
            if (world?.RegisteredPlayers == null) return false;
            for (int i = 0; i < world.RegisteredPlayers.Count; i++)
                if (world.RegisteredPlayers[i] is Player player && player != null && player.IsYourPlayer) return true;
            return false;
        }

        private static bool IsHeadlessProcess()
        {
            string commandLine = Environment.CommandLine ?? string.Empty;
            return commandLine.IndexOf("headless", StringComparison.OrdinalIgnoreCase) >= 0
                || commandLine.IndexOf("batchmode", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private enum LoadingPhase
        {
            StartupOrMenu,
            RaidLoading,
            InRaid
        }

        private readonly struct RequestSample
        {
            internal RequestSample(LoadingPhase phase, string method, string path, double elapsedMs, string error)
            {
                Phase = phase;
                Method = method;
                Path = path;
                ElapsedMs = elapsedMs;
                Error = error;
            }

            internal LoadingPhase Phase { get; }
            internal string Method { get; }
            internal string Path { get; }
            internal double ElapsedMs { get; }
            internal string Error { get; }
        }

        private sealed class LoadingStageSample
        {
            internal LoadingStageSample(string name, double startSeconds)
            {
                Name = name;
                StartSeconds = startSeconds;
                EndSeconds = startSeconds;
            }
            internal string Name { get; }
            internal double StartSeconds { get; }
            internal double EndSeconds { get; set; }
            internal float LastProgress { get; set; }
        }

        private readonly struct LoadingSnapshot
        {
            private LoadingSnapshot(int uploadTime, int uploadBuffer, bool persistent, UnityThreadPriority loadingPriority,
                int minWorkers, int minIo, int unityJobWorkers, ProcessPriorityClass processPriority, SystemThreadPriority mainThreadPriority)
            {
                UploadTime = uploadTime;
                UploadBuffer = uploadBuffer;
                Persistent = persistent;
                LoadingPriority = loadingPriority;
                MinWorkerThreads = minWorkers;
                MinIoThreads = minIo;
                UnityJobWorkers = unityJobWorkers;
                ProcessPriority = processPriority;
                MainThreadPriority = mainThreadPriority;
            }

            internal int MinWorkerThreads { get; }
            internal int MinIoThreads { get; }
            private int UnityJobWorkers { get; }
            private int UploadTime { get; }
            private int UploadBuffer { get; }
            private bool Persistent { get; }
            private UnityThreadPriority LoadingPriority { get; }
            private ProcessPriorityClass ProcessPriority { get; }
            private SystemThreadPriority MainThreadPriority { get; }

            internal static LoadingSnapshot Capture()
            {
                ThreadPool.GetMinThreads(out int workers, out int io);
                ProcessPriorityClass priority;
                try { priority = Process.GetCurrentProcess().PriorityClass; }
                catch { priority = ProcessPriorityClass.Normal; }
                return new LoadingSnapshot(QualitySettings.asyncUploadTimeSlice, QualitySettings.asyncUploadBufferSize,
                    QualitySettings.asyncUploadPersistentBuffer, Application.backgroundLoadingPriority, workers, io, JobsUtility.JobWorkerCount,
                    priority, Thread.CurrentThread.Priority);
            }

            internal void Apply()
            {
                QualitySettings.asyncUploadTimeSlice = UploadTime;
                QualitySettings.asyncUploadBufferSize = UploadBuffer;
                QualitySettings.asyncUploadPersistentBuffer = Persistent;
                Application.backgroundLoadingPriority = LoadingPriority;
                ThreadPool.SetMinThreads(MinWorkerThreads, MinIoThreads);
                if (UnityJobWorkers >= 0 && UnityJobWorkers <= JobsUtility.JobWorkerMaximumCount)
                    JobsUtility.JobWorkerCount = UnityJobWorkers;
                try { Process.GetCurrentProcess().PriorityClass = ProcessPriority; }
                catch { }
                try { Thread.CurrentThread.Priority = MainThreadPriority; }
                catch { }
            }
        }
    }

    public static class LoadingStagePatches
    {
        public static void MapLoadingPostfix(float pr) => Plugin.RecordLoadingProgress("Map loading / preparing", pr);
        public static void MapCachingPostfix(float totalProgress) => Plugin.RecordLoadingProgress("Map culling cache", totalProgress);
        public static void LootProgressPostfix(LoadingProgressStruct p) => Plugin.RecordLoadingProgress(p.Stage.ToString(), p.Progress);
    }

    public static class LoadingOptimizationPatches
    {
        public static bool ConcurrentSceneBundlePreloadPrefix(GClass2287 __instance, IList<ResourceKey> scenes,
            IProgress<float> progress, ref Task __result)
        {
            if (scenes == null || !Plugin.ShouldPreloadSceneBundles(scenes.Count)) return true;
            __result = LoadScenesWithConcurrentBundlePreload(__instance, scenes, progress);
            return false;
        }

        private static async Task LoadScenesWithConcurrentBundlePreload(GClass2287 loader, IList<ResourceKey> scenes,
            IProgress<float> progress)
        {
            LoadSceneMode firstMode = loader.Bool_1 ? LoadSceneMode.Single : LoadSceneMode.Additive;
            GClass3968 first = LoadSceneClass.LoadScene(loader.IAssetsManager, scenes[0], firstMode, loader.Bool_1 || loader.Bool_3);
            loader.List_0.Add(first);
            await first;
            loader.Iprogress_0?.Report(0.5f);

            if (loader.CancellationToken_0.IsCancellationRequested) return;

            long preloadStarted = Stopwatch.GetTimestamp();
            int preloaded = 0;
            HashSet<string> preloadedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AssetsManagerClass concreteManager = null;
            if (loader.IAssetsManager is AssetsManagerClass assetsManager)
            {
                concreteManager = assetsManager;
                int concurrency = Plugin.SceneBundlePreloadConcurrency;
                List<int> pending = Enumerable.Range(1, scenes.Count - 1).ToList();
                while (pending.Count > 0 && !loader.CancellationToken_0.IsCancellationRequested)
                {
                    // BundlesManager counts references when a dependency is already loaded, but an in-flight
                    // dependency operation is shared without incrementing that count. Never overlap dependency
                    // trees inside a batch, so the concurrent preload has the same reference counts as EFT's
                    // original ordered loader.
                    HashSet<string> occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    List<int> batch = new List<int>(concurrency);
                    for (int pendingIndex = 0; pendingIndex < pending.Count && batch.Count < concurrency;)
                    {
                        int sceneIndex = pending[pendingIndex];
                        HashSet<string> footprint = GetBundleFootprint(assetsManager, scenes[sceneIndex].path);
                        if (batch.Count == 0 || !footprint.Overlaps(occupied))
                        {
                            batch.Add(sceneIndex);
                            occupied.UnionWith(footprint);
                            pending.RemoveAt(pendingIndex);
                        }
                        else
                        {
                            pendingIndex++;
                        }
                    }

                    List<IOperation<AssetBundle>> operations = new List<IOperation<AssetBundle>>(batch.Count);
                    for (int index = 0; index < batch.Count; index++)
                        operations.Add(assetsManager.BundlesManager.LoadBundleAsync(scenes[batch[index]].path, logErrors: false));

                    for (int index = 0; index < operations.Count; index++)
                    {
                        await operations[index];
                        if (operations[index].Succeed)
                        {
                            preloadedRoots.Add(scenes[batch[index]].path);
                            preloaded++;
                        }
                    }
                }
            }
            Plugin.RecordSceneBundlePreload(preloaded, Stopwatch.GetTimestamp() - preloadStarted);

            // Unity scene operations deliberately remain serial. EFT requests activation=false while preparing
            // additive scenes, and Unity stalls its AsyncOperation queue behind such an operation.
            for (int index = 1; index < scenes.Count; index++)
            {
                if (loader.CancellationToken_0.IsCancellationRequested) break;
                GClass3968 scene = LoadSceneClass.LoadScene(loader.IAssetsManager, scenes[index], LoadSceneMode.Additive,
                    allowSceneActivation: false);
                loader.List_0.Add(scene);

                // LoadScene synchronously acquires its own root-bundle reference before its first incomplete
                // await. Release the preload's root reference now; dependencies retain the exact references
                // acquired by the dependency-disjoint preload batches.
                if (concreteManager != null && preloadedRoots.Contains(scenes[index].path))
                    concreteManager.BundlesManager.UnloadBundle(scenes[index].path, unloadAllLoadedObjects: false);

                await scene;
                if (progress != null && !loader.CancellationToken_0.IsCancellationRequested)
                    progress.Report(loader.Bool_3 ? 0.5f : 1f);
            }
        }

        private static HashSet<string> GetBundleFootprint(AssetsManagerClass assetsManager, string bundleName)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { bundleName };
            try { result.UnionWith(assetsManager.BundlesManager.FindDependences(bundleName)); }
            catch { }
            return result;
        }

        public static bool ParallelResourceClassificationPrefix(PoolManagerClass.Class1448 __instance,
            ICollection<ResourceKey> resources, ref List<PoolManagerClass.GStruct281> __result)
        {
            if (resources == null || !Plugin.ShouldParallelizeResourceClassification(resources.Count)) return true;

            ResourceKey[] unique = resources.Distinct().ToArray();
            if (!Plugin.ShouldParallelizeResourceClassification(unique.Length)) return true;

            long started = Stopwatch.GetTimestamp();
            try
            {
                PoolManagerClass.GStruct281[] prepared = new PoolManagerClass.GStruct281[unique.Length];
                Parallel.For(0, unique.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Plugin.ResourceClassificationWorkers
                }, index => prepared[index] = __instance.method_1(unique[index]));
                __result = new List<PoolManagerClass.GStruct281>(prepared);
                Plugin.RecordParallelClassification(unique.Length, Stopwatch.GetTimestamp() - started);
                return false;
            }
            catch
            {
                // The replacement only performs read-only lookups. If a mod changes that assumption,
                // fail open and let EFT run its original single-threaded implementation.
                return true;
            }
        }

        public static bool ParallelLootSerializationPrefix(IEnumerable<LootItemPositionClass> lootData,
            ISearchController searchController, ref GClass1947 __result)
        {
            if (lootData == null) return true;
            LootItemPositionClass[] source = lootData as LootItemPositionClass[] ?? lootData.ToArray();
            if (!Plugin.ShouldParallelizeLootSerialization(source.Length)) return true;

            long started = Stopwatch.GetTimestamp();
            try
            {
                GClass1945[] serialized = new GClass1945[source.Length];
                Parallel.For(0, source.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Plugin.ResourceClassificationWorkers
                }, index => serialized[index] = EFTItemSerializerClass.smethod_3(source[index], searchController));
                __result = new GClass1947 { Items = new List<GClass1945>(serialized) };
                Plugin.RecordParallelLootSerialization(source.Length, Stopwatch.GetTimestamp() - started);
                return false;
            }
            catch
            {
                // Serialization only reads independent loot trees. A mod that violates that assumption
                // gets the original EFT implementation instead of preventing the raid from loading.
                return true;
            }
        }

        public static bool FastStaticLootMatchingPrefix(GClass1404 lootItems, ref GInterface30[] staticLootSpawns)
        {
            if (!Plugin.ShouldUseFastStaticLootMatching || lootItems == null || staticLootSpawns == null) return true;

            long started = Stopwatch.GetTimestamp();
            try
            {
                HashSet<string> presentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (LootItemPositionClass lootItem in lootItems)
                    if (lootItem != null && lootItem.Id != null) presentIds.Add(lootItem.Id);

                for (int index = 0; index < staticLootSpawns.Length; index++)
                {
                    GInterface30 staticLoot = staticLootSpawns[index];
                    if (staticLoot == null || presentIds.Contains(staticLoot.Id)) continue;

                    MonoBehaviour behaviour = staticLoot as MonoBehaviour;
                    GameObject gameObject = behaviour == null ? null : behaviour.gameObject;
                    UnityEngine.Debug.LogWarning("Static loot " + staticLoot.Id + " not found in raid data; disabling its linked scene objects.", gameObject);
                    if (staticLoot is LootableContainer container && container.GameObjectsToDestroy != null)
                    {
                        foreach (GameObject target in container.GameObjectsToDestroy)
                            if (target != null) target.SetActive(false);
                    }
                }

                Plugin.RecordStaticLootMatching(lootItems.Count + staticLootSpawns.Length, Stopwatch.GetTimestamp() - started);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    public static class RequestPatches
    {
        public static bool GetDataPrefix(string path, ref byte[] __result)
        {
            long started = Plugin.BeginRequest();
            Exception error = null;
            try { __result = DedicatedRequestWorker.Run(() => RequestHandler.GetDataAsync(path).ConfigureAwait(false).GetAwaiter().GetResult()); }
            catch (Exception ex) { error = ex; throw; }
            finally { Plugin.EndRequest("GET", path, started, error); }
            return false;
        }

        public static bool GetJsonPrefix(string path, ref string __result)
        {
            long started = Plugin.BeginRequest();
            Exception error = null;
            try { __result = DedicatedRequestWorker.Run(() => RequestHandler.GetJsonAsync(path).ConfigureAwait(false).GetAwaiter().GetResult()); }
            catch (Exception ex) { error = ex; throw; }
            finally { Plugin.EndRequest("GET", path, started, error); }
            return false;
        }

        public static bool PostJsonPrefix(string path, string json, ref string __result)
        {
            long started = Plugin.BeginRequest();
            Exception error = null;
            try { __result = DedicatedRequestWorker.Run(() => RequestHandler.PostJsonAsync(path, json).ConfigureAwait(false).GetAwaiter().GetResult()); }
            catch (Exception ex) { error = ex; throw; }
            finally { Plugin.EndRequest("POST", path, started, error); }
            return false;
        }

        public static bool PutJsonPrefix(string path, string json, ref string __result)
        {
            long started = Plugin.BeginRequest();
            Exception error = null;
            try { __result = DedicatedRequestWorker.Run(() => RequestHandler.PutJsonAsync(path, json).ConfigureAwait(false).GetAwaiter().GetResult()); }
            catch (Exception ex) { error = ex; throw; }
            finally { Plugin.EndRequest("PUT", path, started, error); }
            return false;
        }
    }

    internal static class DedicatedRequestWorker
    {
        private static readonly object Gate = new object();
        private static BlockingCollection<IRequestWorkItem> _queue;
        private static Thread[] _workers;
        private static bool _stopping;

        internal static void Initialize(int workerCount)
        {
            lock (Gate)
            {
                if (_queue != null) return;
                _stopping = false;
                _queue = new BlockingCollection<IRequestWorkItem>(new ConcurrentQueue<IRequestWorkItem>());
                _workers = new Thread[workerCount];
                for (int i = 0; i < workerCount; i++)
                {
                    Thread worker = new Thread(WorkerLoop)
                    {
                        IsBackground = true,
                        Name = "TPS Loading HTTP " + (i + 1),
                        Priority = SystemThreadPriority.AboveNormal
                    };
                    _workers[i] = worker;
                    worker.Start();
                }
            }
        }

        internal static T Run<T>(Func<T> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            BlockingCollection<IRequestWorkItem> queue = _queue;
            if (queue == null || _stopping) return callback();

            RequestWorkItem<T> item = new RequestWorkItem<T>(callback);
            queue.Add(item);
            return item.WaitAndGetResult();
        }

        internal static void Shutdown()
        {
            Thread[] workers;
            lock (Gate)
            {
                if (_queue == null) return;
                _stopping = true;
                _queue.CompleteAdding();
                workers = _workers;
            }

            if (workers != null)
            {
                for (int i = 0; i < workers.Length; i++)
                {
                    try { workers[i]?.Join(250); }
                    catch { }
                }
            }

            lock (Gate)
            {
                _queue?.Dispose();
                _queue = null;
                _workers = null;
            }
        }

        private static void WorkerLoop()
        {
            BlockingCollection<IRequestWorkItem> queue = _queue;
            if (queue == null) return;
            try
            {
                foreach (IRequestWorkItem item in queue.GetConsumingEnumerable()) item.Execute();
            }
            catch (ObjectDisposedException) { }
        }

        private interface IRequestWorkItem
        {
            void Execute();
        }

        private sealed class RequestWorkItem<T> : IRequestWorkItem
        {
            private readonly Func<T> _callback;
            private readonly ManualResetEvent _completed = new ManualResetEvent(false);
            private T _result;
            private ExceptionDispatchInfo _error;

            internal RequestWorkItem(Func<T> callback) => _callback = callback;

            public void Execute()
            {
                try { _result = _callback(); }
                catch (Exception ex) { _error = ExceptionDispatchInfo.Capture(ex); }
                finally { _completed.Set(); }
            }

            internal T WaitAndGetResult()
            {
                try
                {
                    _completed.WaitOne();
                    _error?.Throw();
                    return _result;
                }
                finally { _completed.Dispose(); }
            }
        }
    }
}
