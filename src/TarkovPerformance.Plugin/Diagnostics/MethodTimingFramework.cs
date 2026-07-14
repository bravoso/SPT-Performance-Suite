using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using TarkovPerformanceSuite.Utilities;

namespace TarkovPerformanceSuite.RuntimeDiagnostics
{
    internal readonly struct TimingPatchState
    {
        internal TimingPatchState(long started, MethodTimingTarget target) { Started = started; Target = target; }
        internal long Started { get; }
        internal MethodTimingTarget Target { get; }
    }

    internal sealed class MethodTimingTarget
    {
        internal readonly long[] Histogram = new long[64];
        internal MethodBase Method;
        internal string DisplayName;
        internal string Signature;
        internal string SignatureHash;
        internal string IlHash;
        internal string ExistingOwners;
        internal long TotalCalls;
        internal long TotalTicks;
        internal long FrameTicks;
        internal long LastFrameTicks;
        internal long MaximumTicks;
        internal long LastSnapshotCalls;
        internal double LastSnapshotTime;
        internal double CallsPerSecond;

        internal void Record(long elapsed)
        {
            Interlocked.Increment(ref TotalCalls);
            Interlocked.Add(ref TotalTicks, elapsed);
            Interlocked.Add(ref FrameTicks, elapsed);
            long current;
            while (elapsed > (current = Interlocked.Read(ref MaximumTicks)) && Interlocked.CompareExchange(ref MaximumTicks, elapsed, current) != current) { }
            long micros = elapsed * 1000000L / Stopwatch.Frequency;
            int bucket = 0;
            while (micros > 1 && bucket < Histogram.Length - 1) { micros >>= 1; bucket++; }
            Interlocked.Increment(ref Histogram[bucket]);
        }

        internal double P95Milliseconds()
        {
            long calls = Interlocked.Read(ref TotalCalls);
            if (calls == 0) return 0;
            long threshold = (long)Math.Ceiling(calls * 0.95);
            long cumulative = 0;
            for (int i = 0; i < Histogram.Length; i++)
            {
                cumulative += Interlocked.Read(ref Histogram[i]);
                if (cumulative >= threshold) return Math.Pow(2, i) / 1000.0;
            }
            return 0;
        }
    }

    internal sealed class MethodTimingFramework
    {
        internal const string HarmonyId = "com.lucaswilluweit.tarkovperformancesuite.timing";
        private static readonly Dictionary<MethodBase, MethodTimingTarget> Lookup = new Dictionary<MethodBase, MethodTimingTarget>();
        private static volatile bool _active;
        private static int _patchFailures;
        private static volatile bool _circuitOpen;

        private readonly List<MethodTimingTarget> _targets = new List<MethodTimingTarget>(8);
        private readonly StringBuilder _overlayBuilder = new StringBuilder(1024);
        private readonly StringBuilder _reportBuilder = new StringBuilder(4096);
        private readonly MethodTimingTarget[] _sortBuffer = new MethodTimingTarget[32];
        private readonly ManualLogSource _logger;
        private readonly Harmony _harmony = new Harmony(HarmonyId);
        private string _overlayText = string.Empty;
        private string _patchReport = string.Empty;
        private double _nextOverlayRefresh;
        private bool _initialized;
        private bool _circuitLogged;

        internal MethodTimingFramework(ManualLogSource logger) { _logger = logger; }
        internal string PatchReport => _patchReport;
        internal bool Enabled => _active && !_circuitOpen;

        internal void SetRuntimeEnabled(bool enabled)
        {
            _active = enabled && _initialized && _targets.Count > 0 && !_circuitOpen;
        }

        internal void Initialize(bool enabled)
        {
            if (!enabled || _initialized) return;
            _initialized = true;
            Lookup.Clear();
            _targets.Clear();
            _reportBuilder.Clear();
            _reportBuilder.AppendLine("Harmony ID: " + HarmonyId);
            AddTarget(typeof(PlayerBody), "UpdatePlayerRenders", typeof(void), new[] { typeof(EPointOfView), typeof(EPlayerSide) }, "EFT.PlayerBody.UpdatePlayerRenders");
            AddTarget(typeof(PlayerBody), "IsVisible", typeof(bool), Type.EmptyTypes, "EFT.PlayerBody.IsVisible");
            AddTarget(typeof(GameWorld), "Update", typeof(void), Type.EmptyTypes, "EFT.GameWorld.Update");
            AddTarget(typeof(GameWorld), "LateUpdateWorld", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.LateUpdateWorld");
            AddTarget(typeof(Player), "UpdateTick", typeof(void), Type.EmptyTypes, "EFT.Player.UpdateTick");
            AddTarget(typeof(Player), "FixedUpdateTick", typeof(void), Type.EmptyTypes, "EFT.Player.FixedUpdateTick");
            AddTarget(typeof(Player), "LateUpdate", typeof(void), Type.EmptyTypes, "EFT.Player.LateUpdate");
            AddTarget(typeof(Player), "VisualPass", typeof(void), Type.EmptyTypes, "EFT.Player.VisualPass");
            AddTarget(typeof(Player), "ComplexUpdate", typeof(void), new[] { typeof(EUpdateQueue), typeof(float) }, "EFT.Player.ComplexUpdate");
            AddTarget(typeof(Player), "ComplexLateUpdate", typeof(void), new[] { typeof(EUpdateQueue), typeof(float) }, "EFT.Player.ComplexLateUpdate");
            AddTarget(typeof(Player), "ArmsUpdate", typeof(void), new[] { typeof(float) }, "EFT.Player.ArmsUpdate");
            AddTarget(typeof(Player), "BodyUpdate", typeof(void), new[] { typeof(float), typeof(int) }, "EFT.Player.BodyUpdate");
            AddTarget(typeof(Player), "ManualUpdate", typeof(void), new[] { typeof(float), typeof(float?), typeof(int) }, "EFT.Player.ManualUpdate");
            AddTarget(typeof(Player), "FBBIKUpdate", typeof(void), new[] { typeof(float) }, "EFT.Player.FBBIKUpdate");
            AddOptionalFikaTargets();
            _active = _targets.Count > 0;
            _patchReport = _reportBuilder.ToString();
            _logger.LogInfo($"Method timing enabled: {_targets.Count} verified targets patched. No invocation-level logging is performed.");
        }

        internal void FrameBoundary(double now)
        {
            if (!_active) return;
            for (int i = 0; i < _targets.Count; i++) _targets[i].LastFrameTicks = Interlocked.Exchange(ref _targets[i].FrameTicks, 0);
            if (_circuitOpen && !_circuitLogged)
            {
                _circuitLogged = true;
                _logger.LogError("Method timing circuit breaker opened after three patch-side exceptions; original methods continue normally.");
            }
        }

        internal string GetOverlayText(double now)
        {
            if (!_active) return "Method timing: disabled";
            if (_circuitOpen) return "Method timing: circuit breaker open";
            if (now < _nextOverlayRefresh) return _overlayText;
            _nextOverlayRefresh = now + 0.5;
            _overlayBuilder.Clear();
            _overlayBuilder.AppendLine("Top instrumented methods:");
            int targetCount = Math.Min(_targets.Count, _sortBuffer.Length);
            for (int i = 0; i < _targets.Count; i++)
            {
                MethodTimingTarget target = _targets[i];
                long calls = Interlocked.Read(ref target.TotalCalls);
                long deltaCalls = calls - target.LastSnapshotCalls;
                double deltaTime = now - target.LastSnapshotTime;
                if (deltaTime > 0) target.CallsPerSecond = deltaCalls / deltaTime;
                target.LastSnapshotCalls = calls;
                target.LastSnapshotTime = now;
                if (i < targetCount) _sortBuffer[i] = target;
            }
            for (int i = 1; i < targetCount; i++)
            {
                MethodTimingTarget value = _sortBuffer[i];
                int j = i - 1;
                while (j >= 0 && _sortBuffer[j].LastFrameTicks < value.LastFrameTicks)
                {
                    _sortBuffer[j + 1] = _sortBuffer[j];
                    j--;
                }
                _sortBuffer[j + 1] = value;
            }
            int shown = Math.Min(targetCount, 12);
            for (int i = 0; i < shown; i++)
            {
                MethodTimingTarget target = _sortBuffer[i];
                long calls = Interlocked.Read(ref target.TotalCalls);
                long ticks = Interlocked.Read(ref target.TotalTicks);
                double averageMs = calls > 0 ? ticks * 1000.0 / Stopwatch.Frequency / calls : 0;
                double maximumMs = Interlocked.Read(ref target.MaximumTicks) * 1000.0 / Stopwatch.Frequency;
                double frameMs = target.LastFrameTicks * 1000.0 / Stopwatch.Frequency;
                _overlayBuilder.Append("  ").Append(target.DisplayName).Append(": ").Append(target.CallsPerSecond.ToString("F1")).Append("/s | frame ")
                    .Append(frameMs.ToString("F3")).Append(" ms | avg ").Append(averageMs.ToString("F4")).Append(" | max ")
                    .Append(maximumMs.ToString("F3")).Append(" | p95~ ").Append(target.P95Milliseconds().ToString("F3")).AppendLine(" ms");
            }
            _overlayText = _overlayBuilder.ToString();
            return _overlayText;
        }

        internal string GetDiagnosticSnapshot(double now)
        {
            _nextOverlayRefresh = 0;
            return GetOverlayText(now);
        }

        internal void ResetAggregates(double now)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                MethodTimingTarget target = _targets[i];
                Interlocked.Exchange(ref target.TotalCalls, 0);
                Interlocked.Exchange(ref target.TotalTicks, 0);
                Interlocked.Exchange(ref target.FrameTicks, 0);
                target.LastFrameTicks = 0;
                Interlocked.Exchange(ref target.MaximumTicks, 0);
                for (int bucket = 0; bucket < target.Histogram.Length; bucket++) Interlocked.Exchange(ref target.Histogram[bucket], 0);
                target.LastSnapshotCalls = 0;
                target.LastSnapshotTime = now;
                target.CallsPerSecond = 0;
            }
            _overlayText = string.Empty;
            _nextOverlayRefresh = 0;
        }

        internal void Shutdown()
        {
            _active = false;
            if (_initialized) _harmony.UnpatchSelf();
            Lookup.Clear();
            _targets.Clear();
            _initialized = false;
        }

        private void AddOptionalFikaTargets()
        {
            Assembly fika = FindAssembly("Fika.Core");
            if (fika == null) { _reportBuilder.AppendLine("Fika.Core: not loaded; optional targets not patched."); return; }
            Type client = fika.GetType("Fika.Core.Networking.FikaClient", false);
            Type observed = fika.GetType("Fika.Core.Main.Players.ObservedPlayer", false);
            Type fikaPlayer = fika.GetType("Fika.Core.Main.Players.FikaPlayer", false);
            AddTarget(client, "Update", typeof(void), Type.EmptyTypes, "FikaClient.Update");
            AddTarget(observed, "ManualStateUpdate", typeof(void), new[] { typeof(double) }, "ObservedPlayer.ManualStateUpdate");
            AddTarget(fikaPlayer, "ManualUpdate", typeof(void), new[] { typeof(float), typeof(float?), typeof(int) }, "FikaPlayer.ManualUpdate");
        }

        private void AddTarget(Type type, string methodName, Type expectedReturn, Type[] parameters, string displayName)
        {
            if (type == null) { _reportBuilder.AppendLine(displayName + ": type not found; skipped."); return; }
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
            if (method == null || method.ReturnType != expectedReturn)
            {
                _reportBuilder.AppendLine(displayName + ": expected signature not found; skipped.");
                return;
            }

            try
            {
                Patches existing = Harmony.GetPatchInfo(method);
                string owners = existing == null || existing.Owners.Count == 0 ? "none" : string.Join(",", existing.Owners);
                var target = new MethodTimingTarget
                {
                    Method = method,
                    DisplayName = displayName,
                    Signature = MethodSignatureFingerprint.Describe(method),
                    SignatureHash = MethodSignatureFingerprint.Sha256(method),
                    IlHash = HashIl(method),
                    ExistingOwners = owners
                };
                var prefix = new HarmonyMethod(typeof(MethodTimingFramework).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                var postfix = new HarmonyMethod(typeof(MethodTimingFramework).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
                _harmony.Patch(method, prefix, postfix);
                Lookup.Add(method, target);
                _targets.Add(target);
                string access = method.IsPublic ? "public" : method.IsPrivate ? "private" : method.IsFamily ? "protected" : "internal";
                _reportBuilder.AppendLine($"{displayName} | {method.Module.Assembly.GetName().Name} | {target.Signature} | {access} | virtual={method.IsVirtual} | signatureSHA256={target.SignatureHash} | ilSHA256={target.IlHash} | existingOwners={owners}");
                _logger.LogInfo($"Timing patch target verified: {target.Signature}; assembly={method.Module.Assembly.FullName}; existing owners={owners}");
            }
            catch (Exception ex)
            {
                _reportBuilder.AppendLine(displayName + ": patch failed open; " + ex.GetType().Name + ": " + ex.Message);
                _logger.LogError("Timing target failed open and was skipped: " + displayName + ": " + ex);
            }
        }

        private static void Prefix(MethodBase __originalMethod, out TimingPatchState __state)
        {
            __state = default;
            if (!_active || _circuitOpen) return;
            try
            {
                if (Lookup.TryGetValue(__originalMethod, out MethodTimingTarget target)) __state = new TimingPatchState(Stopwatch.GetTimestamp(), target);
            }
            catch { PatchFailure(); }
        }

        private static void Postfix(TimingPatchState __state)
        {
            if (__state.Target == null || _circuitOpen) return;
            try { __state.Target.Record(Stopwatch.GetTimestamp() - __state.Started); }
            catch { PatchFailure(); }
        }

        private static void PatchFailure()
        {
            if (Interlocked.Increment(ref _patchFailures) >= 3) { _circuitOpen = true; _active = false; }
        }

        private static Assembly FindAssembly(string name)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++) if (string.Equals(assemblies[i].GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return assemblies[i];
            return null;
        }

        private static string HashIl(MethodInfo method)
        {
            try
            {
                byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null) return "unavailable";
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes);
                    var builder = new StringBuilder(64);
                    for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                    return builder.ToString();
                }
            }
            catch { return "unavailable"; }
        }
    }
}
