using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal readonly struct RemoteUpdateBudgetCounters
    {
        internal RemoteUpdateBudgetCounters(int remote, int hidden, int budgeted, int animators, long skippedProps, long skippedTriggers, long skippedPresentation, double averageMs)
        {
            RemoteCharacters = remote;
            HiddenCharacters = hidden;
            BudgetedCharacters = budgeted;
            CulledAnimators = animators;
            SkippedPropUpdates = skippedProps;
            SkippedTriggerSearches = skippedTriggers;
            SkippedPresentationUpdates = skippedPresentation;
            AverageMs = averageMs;
        }

        internal int RemoteCharacters { get; }
        internal int HiddenCharacters { get; }
        internal int BudgetedCharacters { get; }
        internal int CulledAnimators { get; }
        internal long SkippedPropUpdates { get; }
        internal long SkippedTriggerSearches { get; }
        internal long SkippedPresentationUpdates { get; }
        internal double AverageMs { get; }
    }

    internal sealed class RemoteUpdateBudgetFeature : IPerformanceFeature
    {
        private static RemoteUpdateBudgetFeature _instance;
        [ThreadStatic] private static int _allowedComplexLatePlayerId;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly EntityRegistry _registry;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private readonly HashSet<int> _budgetedIds = new HashSet<int>();
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        private readonly Dictionary<int, float> _hiddenSince = new Dictionary<int, float>(64);
        private readonly Dictionary<int, int> _propCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _triggerCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _armsCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _bodyCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _ikCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<int, int> _complexLateCallCounters = new Dictionary<int, int>(64);
        private readonly Dictionary<Animator, AnimatorCullingMode> _animatorStates = new Dictionary<Animator, AnimatorCullingMode>(256);
        private readonly HashSet<Animator> _seenAnimators = new HashSet<Animator>();
        private readonly List<Animator> _animatorRestore = new List<Animator>(64);
        private readonly List<int> _idRestore = new List<int>(16);
        private Harmony _harmony;
        private MethodInfo _propUpdate;
        private MethodInfo _triggerSearch;
        private MethodInfo _armsUpdate;
        private MethodInfo _bodyUpdate;
        private MethodInfo _fbbikUpdate;
        private MethodInfo _complexLateUpdate;
        private MethodInfo _observedVisualPass;
        private MethodInfo _observedFbbikUpdate;
        private bool _raidActive;
        private bool _patchesInstalled;
        private float _nextUpdate;
        private long _skippedProps;
        private long _skippedTriggers;
        private long _skippedPresentation;
        private double _averageMs;
        private RemoteUpdateBudgetCounters _counters;

        internal RemoteUpdateBudgetFeature(ManualLogSource logger, PluginConfiguration configuration, EntityRegistry registry, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _registry = registry;
            _exceptions = exceptions;
        }

        public string Name => "Remote Character CPU Budget";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal RemoteUpdateBudgetCounters Counters => _counters;
        internal string StatusText => IsEnabled
            ? (_patchesInstalled ? "enabled" : "animator-only")
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            _instance = this;
            InstallPatches();
            SetEnabled(_configuration.RemoteUpdateBudgetEnabled.Value);
        }

        public void OnRaidStarted()
        {
            InstallOptionalFikaPatches();
            _raidActive = true;
            _nextUpdate = 0;
            _skippedProps = 0;
            _skippedTriggers = 0;
            _skippedPresentation = 0;
            _counters = default;
        }

        public void OnRaidEnded()
        {
            _raidActive = false;
            RestoreAll();
            _counters = default;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.RemoteUpdateBudgetEnabled.Value = enabled;
            if (!enabled) RestoreAll();
            _logger.LogInfo(Name + " " + (enabled ? "enabled" : "disabled") + ". It only budgets presentation work for remote characters already reported hidden by EFT/Fika culling.");
        }

        public void Shutdown()
        {
            _raidActive = false;
            IsEnabled = false;
            RestoreAll();
            if (_harmony != null)
            {
                if (_propUpdate != null) _harmony.Unpatch(_propUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                if (_triggerSearch != null) _harmony.Unpatch(_triggerSearch, HarmonyPatchType.Prefix, _harmony.Id);
                if (_armsUpdate != null) _harmony.Unpatch(_armsUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                if (_bodyUpdate != null) _harmony.Unpatch(_bodyUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                if (_fbbikUpdate != null) _harmony.Unpatch(_fbbikUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                if (_complexLateUpdate != null)
                {
                    _harmony.Unpatch(_complexLateUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                    _harmony.Unpatch(_complexLateUpdate, HarmonyPatchType.Postfix, _harmony.Id);
                }
                if (_observedVisualPass != null)
                {
                    _harmony.Unpatch(_observedVisualPass, HarmonyPatchType.Prefix, _harmony.Id);
                    _harmony.Unpatch(_observedVisualPass, HarmonyPatchType.Postfix, _harmony.Id);
                }
                if (_observedFbbikUpdate != null)
                {
                    _harmony.Unpatch(_observedFbbikUpdate, HarmonyPatchType.Prefix, _harmony.Id);
                    _harmony.Unpatch(_observedFbbikUpdate, HarmonyPatchType.Postfix, _harmony.Id);
                }
            }
            _patchesInstalled = false;
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        internal void Tick(float now)
        {
            if (!IsEnabled || !_raidActive || now < _nextUpdate) return;
            float interval = Clamp(_configuration.RemoteUpdateBudgetInterval.Value, 0.033f, 0.5f);
            _nextUpdate = now + interval;
            long started = Stopwatch.GetTimestamp();
            try
            {
                Process(now);
                _breaker.Success();
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogError(Name + " failed open: " + ex);
                if (_breaker.Failure())
                {
                    IsEnabled = false;
                    _configuration.RemoteUpdateBudgetEnabled.Value = false;
                    RestoreAll();
                }
            }
            finally
            {
                double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                _averageMs = _averageMs == 0 ? elapsed : (_averageMs * 0.95) + (elapsed * 0.05);
            }
        }

        private void Process(float now)
        {
            float minimumDistance = Clamp(_configuration.RemoteUpdateBudgetDistance.Value, 20f, 200f);
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            float hold = Clamp(_configuration.RemoteUpdateBudgetHold.Value, 0.05f, 2f);
            int remote = 0;
            int hidden = 0;
            _seenIds.Clear();
            _seenAnimators.Clear();

            foreach (TrackedEntity entity in _registry.Entities)
            {
                if ((entity.Kind != EntityKind.RemoteAI && entity.Kind != EntityKind.RemoteHuman) || entity.Player == null) continue;
                remote++;
                int id = entity.Player.GetInstanceID();
                _seenIds.Add(id);
                bool eligible = entity.IsAlive && !entity.IsVisible && entity.DistanceSquared >= minimumDistanceSquared;
                if (!eligible)
                {
                    _hiddenSince.Remove(id);
                    _budgetedIds.Remove(id);
                    _propCallCounters.Remove(id);
                    _triggerCallCounters.Remove(id);
                    _armsCallCounters.Remove(id);
                    _bodyCallCounters.Remove(id);
                    _ikCallCounters.Remove(id);
                    _complexLateCallCounters.Remove(id);
                    RestoreAnimators(entity.Animators);
                    continue;
                }

                hidden++;
                if (!_hiddenSince.TryGetValue(id, out float since))
                {
                    _hiddenSince.Add(id, now);
                    continue;
                }
                if (now - since < hold) continue;

                _budgetedIds.Add(id);
                if (!_configuration.RemoteAnimatorCullingEnabled.Value) continue;
                for (int i = 0; i < entity.Animators.Length; i++)
                {
                    Animator animator = entity.Animators[i];
                    if (animator == null || !animator.isActiveAndEnabled) continue;
                    _seenAnimators.Add(animator);
                    if (!_animatorStates.ContainsKey(animator)) _animatorStates.Add(animator, animator.cullingMode);
                    animator.cullingMode = AnimatorCullingMode.CullCompletely;
                }
            }

            RestoreNoLongerEligible();
            _counters = new RemoteUpdateBudgetCounters(remote, hidden, _budgetedIds.Count, _animatorStates.Count, _skippedProps, _skippedTriggers, _skippedPresentation, _averageMs);
        }

        private bool ShouldRun(Player player, BudgetedUpdate update)
        {
            if (!IsEnabled || !_raidActive || player == null) return true;
            // ComplexLateUpdate contains presentation sub-passes on some EFT builds. If its
            // outer budget allowed this player, do not divide nested arms/body/IK a second time.
            if (update != BudgetedUpdate.ComplexLate && _allowedComplexLatePlayerId == player.GetInstanceID()) return true;
            // Never wait for the amortized registry refresh when EFT/Fika has already
            // promoted the character to visible this frame.
            if (player.IsVisible) return true;
            if (update >= BudgetedUpdate.Arms && !_configuration.RemotePresentationBudgetEnabled.Value) return true;
            if (update == BudgetedUpdate.ComplexLate && !_configuration.RemoteComplexLateUpdateBudgetEnabled.Value) return true;
            int id = player.GetInstanceID();
            if (!_budgetedIds.Contains(id)) return true;
            int divisor = Clamp(_configuration.RemoteUpdateBudgetDivisor.Value, 2, 8);
            Dictionary<int, int> counters = update == BudgetedUpdate.Prop ? _propCallCounters
                : update == BudgetedUpdate.Trigger ? _triggerCallCounters
                : update == BudgetedUpdate.Arms ? _armsCallCounters
                : update == BudgetedUpdate.Body || update == BudgetedUpdate.ObservedVisual ? _bodyCallCounters
                : update == BudgetedUpdate.Ik || update == BudgetedUpdate.ObservedIk ? _ikCallCounters
                : _complexLateCallCounters;
            counters.TryGetValue(id, out int calls);
            calls++;
            bool run = calls >= divisor;
            counters[id] = run ? 0 : calls;
            if (!run)
            {
                if (update == BudgetedUpdate.Prop) _skippedProps++;
                else if (update == BudgetedUpdate.Trigger) _skippedTriggers++;
                else _skippedPresentation++;
            }
            return run;
        }

        private void InstallPatches()
        {
            try
            {
                _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.remote-update-budget");
                _propUpdate = AccessTools.Method(typeof(Player), nameof(Player.PropUpdate));
                _triggerSearch = AccessTools.Method(typeof(Player), nameof(Player.UpdateTriggerColliderSearcher), new[] { typeof(float), typeof(bool) });
                _armsUpdate = AccessTools.Method(typeof(Player), nameof(Player.ArmsUpdate), new[] { typeof(float) });
                _bodyUpdate = AccessTools.Method(typeof(Player), nameof(Player.BodyUpdate), new[] { typeof(float), typeof(int) });
                _fbbikUpdate = AccessTools.Method(typeof(Player), nameof(Player.FBBIKUpdate), new[] { typeof(float) });
                _complexLateUpdate = AccessTools.Method(typeof(Player), nameof(Player.ComplexLateUpdate), new[] { typeof(EUpdateQueue), typeof(float) });
                MethodInfo propPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(PropUpdatePrefix));
                MethodInfo triggerPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(TriggerSearchPrefix));
                MethodInfo armsPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ArmsUpdatePrefix));
                MethodInfo bodyPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(BodyUpdatePrefix));
                MethodInfo ikPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(FbbikUpdatePrefix));
                MethodInfo complexLatePrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ComplexLateUpdatePrefix));
                MethodInfo complexLatePostfix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ComplexLateUpdatePostfix));
                if (_propUpdate == null || _triggerSearch == null || _armsUpdate == null || _bodyUpdate == null || _fbbikUpdate == null || _complexLateUpdate == null
                    || propPrefix == null || triggerPrefix == null || armsPrefix == null || bodyPrefix == null || ikPrefix == null || complexLatePrefix == null || complexLatePostfix == null)
                    throw new MissingMethodException("Expected EFT Player presentation methods were not found.");
                _harmony.Patch(_propUpdate, prefix: new HarmonyMethod(propPrefix));
                _harmony.Patch(_triggerSearch, prefix: new HarmonyMethod(triggerPrefix));
                _harmony.Patch(_armsUpdate, prefix: new HarmonyMethod(armsPrefix));
                _harmony.Patch(_bodyUpdate, prefix: new HarmonyMethod(bodyPrefix));
                _harmony.Patch(_fbbikUpdate, prefix: new HarmonyMethod(ikPrefix));
                _harmony.Patch(_complexLateUpdate, prefix: new HarmonyMethod(complexLatePrefix), postfix: new HarmonyMethod(complexLatePostfix));
                _patchesInstalled = true;
                InstallOptionalFikaPatches();
            }
            catch (Exception ex)
            {
                _patchesInstalled = false;
                _exceptions.Add(Name + " patch install", ex);
                _logger.LogWarning(Name + " method budgeting unavailable; hidden animator culling remains usable. " + ex.Message);
            }
        }

        private static bool PropUpdatePrefix(Player __instance) => _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.Prop);
        private static bool TriggerSearchPrefix(Player __instance) => _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.Trigger);
        private static bool ArmsUpdatePrefix(Player __instance) => _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.Arms);
        private static bool BodyUpdatePrefix(Player __instance) => _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.Body);
        private static bool FbbikUpdatePrefix(Player __instance) => _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.Ik);
        private static bool ComplexLateUpdatePrefix(Player __instance)
        {
            _allowedComplexLatePlayerId = 0;
            bool run = _instance == null || _instance.ShouldRun(__instance, BudgetedUpdate.ComplexLate);
            if (run && __instance != null) _allowedComplexLatePlayerId = __instance.GetInstanceID();
            return run;
        }

        private static void ComplexLateUpdatePostfix() => _allowedComplexLatePlayerId = 0;

        private void InstallOptionalFikaPatches()
        {
            if (_harmony == null || _observedVisualPass != null || _observedFbbikUpdate != null) return;
            try
            {
                Assembly fika = null;
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                    if (string.Equals(assemblies[i].GetName().Name, "Fika.Core", StringComparison.OrdinalIgnoreCase)) { fika = assemblies[i]; break; }
                Type observed = fika?.GetType("Fika.Core.Main.Players.ObservedPlayer", false);
                if (observed == null) return;
                _observedVisualPass = observed.GetMethod("ObservedVisualPass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float), typeof(int) }, null);
                _observedFbbikUpdate = observed.GetMethod("ObservedFBBIKUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float), typeof(int) }, null);
                MethodInfo visualPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ObservedVisualPassPrefix));
                MethodInfo ikPrefix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ObservedFbbikUpdatePrefix));
                MethodInfo outerPostfix = AccessTools.Method(typeof(RemoteUpdateBudgetFeature), nameof(ObservedPresentationPostfix));
                if (_observedVisualPass != null)
                    _harmony.Patch(_observedVisualPass, prefix: new HarmonyMethod(visualPrefix), postfix: new HarmonyMethod(outerPostfix));
                if (_observedFbbikUpdate != null)
                    _harmony.Patch(_observedFbbikUpdate, prefix: new HarmonyMethod(ikPrefix), postfix: new HarmonyMethod(outerPostfix));
                if (_observedVisualPass != null || _observedFbbikUpdate != null)
                    _logger.LogInfo("Remote Character CPU Budget attached directly to Fika observed-player visual/IK presentation passes.");
            }
            catch (Exception ex)
            {
                _observedVisualPass = null;
                _observedFbbikUpdate = null;
                _exceptions.Add(Name + " optional Fika patch install", ex);
                _logger.LogWarning(Name + " optional Fika presentation budgeting unavailable; base EFT budgeting remains active. " + ex.Message);
            }
        }

        private static bool ObservedVisualPassPrefix(Player __instance) => ObservedPresentationPrefix(__instance, BudgetedUpdate.ObservedVisual);
        private static bool ObservedFbbikUpdatePrefix(Player __instance) => ObservedPresentationPrefix(__instance, BudgetedUpdate.ObservedIk);

        private static bool ObservedPresentationPrefix(Player player, BudgetedUpdate update)
        {
            _allowedComplexLatePlayerId = 0;
            bool run = _instance == null || _instance.ShouldRun(player, update);
            if (run && player != null) _allowedComplexLatePlayerId = player.GetInstanceID();
            return run;
        }

        private static void ObservedPresentationPostfix() => _allowedComplexLatePlayerId = 0;

        private void RestoreAnimators(Animator[] animators)
        {
            for (int i = 0; i < animators.Length; i++) RestoreAnimator(animators[i]);
        }

        private void RestoreNoLongerEligible()
        {
            _animatorRestore.Clear();
            foreach (KeyValuePair<Animator, AnimatorCullingMode> pair in _animatorStates)
                if (pair.Key == null || !_seenAnimators.Contains(pair.Key)) _animatorRestore.Add(pair.Key);
            for (int i = 0; i < _animatorRestore.Count; i++) RestoreAnimator(_animatorRestore[i]);

            _idRestore.Clear();
            foreach (int id in _hiddenSince.Keys) if (!_seenIds.Contains(id)) _idRestore.Add(id);
            for (int i = 0; i < _idRestore.Count; i++)
            {
                _hiddenSince.Remove(_idRestore[i]);
                _budgetedIds.Remove(_idRestore[i]);
                _propCallCounters.Remove(_idRestore[i]);
                _triggerCallCounters.Remove(_idRestore[i]);
                _armsCallCounters.Remove(_idRestore[i]);
                _bodyCallCounters.Remove(_idRestore[i]);
                _ikCallCounters.Remove(_idRestore[i]);
                _complexLateCallCounters.Remove(_idRestore[i]);
            }
        }

        private void RestoreAnimator(Animator animator)
        {
            if (ReferenceEquals(animator, null) || !_animatorStates.TryGetValue(animator, out AnimatorCullingMode original)) return;
            if (animator != null) animator.cullingMode = original;
            _animatorStates.Remove(animator);
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<Animator, AnimatorCullingMode> pair in _animatorStates)
                if (pair.Key != null) pair.Key.cullingMode = pair.Value;
            _animatorStates.Clear();
            _seenAnimators.Clear();
            _animatorRestore.Clear();
            _hiddenSince.Clear();
            _propCallCounters.Clear();
            _triggerCallCounters.Clear();
            _armsCallCounters.Clear();
            _bodyCallCounters.Clear();
            _ikCallCounters.Clear();
            _complexLateCallCounters.Clear();
            _budgetedIds.Clear();
            _seenIds.Clear();
            _idRestore.Clear();
        }

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static float Clamp(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;
        private enum BudgetedUpdate { Prop, Trigger, Arms, Body, Ik, ComplexLate, ObservedVisual, ObservedIk }
    }
}
