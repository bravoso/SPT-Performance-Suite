using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class KnownModCompatibilityFeature : IPerformanceFeature
    {
        private static GameWorld _cachedWorld;
        private static int _replacementCount;
        private static long _fastLookups;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private Harmony _harmony;
        private MethodInfo _botCounterTarget;
        private float _nextDiscovery;
        private int _discoveryAttempts;
        private bool _discoveryComplete;

        internal KnownModCompatibilityFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "Known Mod CPU Compatibility";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal long FastLookups => _fastLookups;
        internal string StatusText
        {
            get
            {
                if (!IsEnabled) return _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";
                if (_botCounterTarget == null) return _discoveryComplete ? "enabled | Bot Counter not installed" : "enabled | checking installed mods";
                return "enabled | Bot Counter full-scene scan removed | cached lookups " + _fastLookups;
            }
        }

        public void Initialize()
        {
            _breaker.Reset();
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.compatibility");
            SetEnabled(_configuration.KnownModFixesEnabled.Value);
        }

        public void OnRaidStarted() { _fastLookups = 0; }
        public void OnRaidEnded() { _cachedWorld = null; }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.KnownModFixesEnabled.Value = enabled;
            if (enabled)
            {
                _discoveryAttempts = 0;
                _discoveryComplete = false;
                TryInstall();
            }
            else Uninstall();
        }

        public void Shutdown()
        {
            IsEnabled = false;
            Uninstall();
            _cachedWorld = null;
        }

        internal void SetWorld(GameWorld world) => _cachedWorld = world;

        internal void Tick(float now)
        {
            if (!IsEnabled || _discoveryComplete || _botCounterTarget != null || now < _nextDiscovery) return;
            _nextDiscovery = now + 1f;
            TryInstall();
        }

        private void TryInstall()
        {
            if (!IsEnabled || _botCounterTarget != null) return;
            try
            {
                BepInEx.PluginInfo pluginInfo = null;
                foreach (KeyValuePair<string, BepInEx.PluginInfo> pair in Chainloader.PluginInfos)
                {
                    if (!string.Equals(pair.Value?.Metadata?.GUID, "com.yourname.botcounter", StringComparison.OrdinalIgnoreCase)) continue;
                    pluginInfo = pair.Value;
                    break;
                }
                if (pluginInfo == null)
                {
                    // Plugin discovery is complete before plugin Awake methods run. Do not repeatedly
                    // scan every loaded type when the optional mod is absent; that caused a 0.5 s
                    // hitch every five seconds on an i7-2600K.
                    _discoveryComplete = true;
                    return;
                }

                Type type = pluginInfo.Instance != null ? pluginInfo.Instance.GetType() : null;
                if (type == null)
                {
                    _discoveryAttempts++;
                    if (_discoveryAttempts < 3) return;
                    throw new TypeLoadException("SPT Detailed Bot Counter is registered but its plugin instance was unavailable.");
                }
                MethodInfo target = AccessTools.Method(type, "UpdateBotClassifications");
                MethodInfo transpiler = AccessTools.Method(typeof(KnownModCompatibilityFeature), nameof(ReplaceGameWorldSearch));
                if (target == null || transpiler == null) throw new MissingMethodException("SPT Detailed Bot Counter 1.7 update method was not found.");
                _replacementCount = 0;
                _harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                if (_replacementCount != 1)
                {
                    _harmony.Unpatch(target, HarmonyPatchType.Transpiler, _harmony.Id);
                    throw new InvalidOperationException("Expected exactly one GameWorld full-scene lookup, found " + _replacementCount + ".");
                }
                _botCounterTarget = target;
                _discoveryComplete = true;
                _breaker.Success();
                _logger.LogWarning("Removed SPT Detailed Bot Counter's repeating Object.FindObjectOfType<GameWorld>() scan. Counts and UI remain unchanged; the suite supplies its cached raid world.");
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogWarning(Name + " failed open; the external mod remains unchanged: " + ex.Message);
                if (_breaker.Failure())
                {
                    IsEnabled = false;
                    _configuration.KnownModFixesEnabled.Value = false;
                }
            }
        }

        private void Uninstall()
        {
            if (_botCounterTarget != null && _harmony != null)
                _harmony.Unpatch(_botCounterTarget, HarmonyPatchType.Transpiler, _harmony.Id);
            _botCounterTarget = null;
            _discoveryComplete = false;
        }

        private static IEnumerable<CodeInstruction> ReplaceGameWorldSearch(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo fastGetter = AccessTools.Method(typeof(KnownModCompatibilityFeature), nameof(GetCachedGameWorld));
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(UnityEngine.Object)
                    && method.Name == "FindObjectOfType"
                    && method.ReturnType == typeof(GameWorld)
                    && method.GetParameters().Length == 0)
                {
                    _replacementCount++;
                    yield return new CodeInstruction(OpCodes.Call, fastGetter).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static GameWorld GetCachedGameWorld()
        {
            _fastLookups++;
            if (_cachedWorld != null) return _cachedWorld;
            return Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        }
    }
}
