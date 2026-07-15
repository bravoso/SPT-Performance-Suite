using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    /// <summary>
    /// Conservative authority-side pacing. Essential state is never removed: Fika continues to
    /// send complete bot snapshots, just at a stable interpolation-friendly cadence, while ORBIT
    /// navigation requests are spread across frames instead of completing in five-job bursts.
    /// </summary>
    internal sealed class HeadlessAuthorityFeature : IPerformanceFeature
    {
        private static HeadlessAuthorityFeature _instance;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private Harmony _harmony;
        private bool _fikaPatched;
        private bool _orbitPatched;
        private object _botStateManager;
        private FieldInfo _botIntervalField;
        private float _originalBotInterval;
        private object _navExecutor;
        private FieldInfo _navBatchField;
        private int _originalNavBatch;
        private float _nextDiscovery;
        private bool _isHeadless;

        internal HeadlessAuthorityFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "Headless Authority Pacing";
        public bool IsAvailable => true;
        public bool IsEnabled { get; private set; }
        internal string StatusText => !_isHeadless ? "client process; authority pacing inactive"
            : (IsEnabled ? "headless active | bot snapshots " + Clamp(_configuration.HeadlessBotStateSendRate.Value, 10, 30)
                + " Hz | ORBIT nav " + Clamp(_configuration.HeadlessOrbitNavJobsPerFrame.Value, 1, 5) + "/frame" : "headless detected; disabled");

        public void Initialize()
        {
            _instance = this;
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.headless-authority");
            SetEnabled(_configuration.HeadlessAuthorityEnabled.Value);
            DiscoverAndPatch();
        }

        internal void Tick(float now)
        {
            if (now < _nextDiscovery) return;
            _nextDiscovery = now + 2f;
            DiscoverAndPatch();
            ApplyCurrentBudgets();
        }

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.HeadlessAuthorityEnabled.Value = enabled;
            if (!enabled) RestoreBudgets();
            else ApplyCurrentBudgets();
        }

        public void OnRaidStarted()
        {
            DiscoverAndPatch();
            ApplyCurrentBudgets();
        }

        public void OnRaidEnded()
        {
            RestoreBudgets();
            _botStateManager = null;
            _botIntervalField = null;
            _navExecutor = null;
            _navBatchField = null;
        }

        public void Shutdown()
        {
            RestoreBudgets();
            try { _harmony?.UnpatchSelf(); } catch { }
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void DiscoverAndPatch()
        {
            try
            {
                Assembly fika = FindAssembly("Fika.Core");
                if (fika != null)
                {
                    Type backend = fika.GetType("Fika.Core.Main.Utils.FikaBackendUtils", false);
                    PropertyInfo headless = backend?.GetProperty("IsHeadless", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (headless != null) _isHeadless = (bool)headless.GetValue(null, null);
                    if (!_fikaPatched)
                    {
                        Type manager = fika.GetType("Fika.Core.Main.Components.BotStateManager", false);
                        MethodInfo create = manager?.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        MethodInfo postfix = typeof(HeadlessAuthorityFeature).GetMethod(nameof(BotManagerCreatedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                        if (create != null && postfix != null)
                        {
                            _harmony.Patch(create, postfix: new HarmonyMethod(postfix));
                            _fikaPatched = true;
                        }
                    }
                }

                Assembly orbit = FindAssembly("ORBIT");
                if (orbit != null && !_orbitPatched)
                {
                    Type executor = orbit.GetType("Orbit.Navigation.NavJobExecutor", false);
                    ConstructorInfo constructor = executor?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(int) }, null);
                    MethodInfo postfix = typeof(HeadlessAuthorityFeature).GetMethod(nameof(NavExecutorCreatedPostfix), BindingFlags.Static | BindingFlags.NonPublic);
                    if (constructor != null && postfix != null)
                    {
                        _harmony.Patch(constructor, postfix: new HarmonyMethod(postfix));
                        _orbitPatched = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name + " discovery", ex);
            }
        }

        private static void BotManagerCreatedPostfix(object __result) => _instance?.CaptureBotManager(__result);
        private static void NavExecutorCreatedPostfix(object __instance) => _instance?.CaptureNavExecutor(__instance);

        private void CaptureBotManager(object manager)
        {
            if (!_isHeadless || manager == null) return;
            _botStateManager = manager;
            _botIntervalField = manager.GetType().GetField("_updatesPerTick", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_botIntervalField != null) _originalBotInterval = (float)_botIntervalField.GetValue(manager);
            ApplyCurrentBudgets();
        }

        private void CaptureNavExecutor(object executor)
        {
            if (!_isHeadless || executor == null) return;
            FieldInfo[] fields = executor.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.FieldType != typeof(int) || field.Name.IndexOf("batchSize", StringComparison.OrdinalIgnoreCase) < 0) continue;
                _navExecutor = executor;
                _navBatchField = field;
                _originalNavBatch = (int)field.GetValue(executor);
                break;
            }
            ApplyCurrentBudgets();
        }

        private void ApplyCurrentBudgets()
        {
            if (!IsEnabled || !_isHeadless) return;
            try
            {
                if (_botStateManager != null && _botIntervalField != null)
                    _botIntervalField.SetValue(_botStateManager, 1f / Clamp(_configuration.HeadlessBotStateSendRate.Value, 10, 30));
                if (_navExecutor != null && _navBatchField != null)
                    _navBatchField.SetValue(_navExecutor, Clamp(_configuration.HeadlessOrbitNavJobsPerFrame.Value, 1, 5));
            }
            catch (Exception ex) { _exceptions.Add(Name + " apply", ex); }
        }

        private void RestoreBudgets()
        {
            try
            {
                if (_botStateManager != null && _botIntervalField != null && _originalBotInterval > 0f)
                    _botIntervalField.SetValue(_botStateManager, _originalBotInterval);
                if (_navExecutor != null && _navBatchField != null && _originalNavBatch > 0)
                    _navBatchField.SetValue(_navExecutor, _originalNavBatch);
            }
            catch { }
        }

        private static Assembly FindAssembly(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
                if (string.Equals(assemblies[i].GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return assemblies[i];
            return null;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
