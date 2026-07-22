using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using Systems.Effects;
using TarkovPerformanceSuite.Utilities;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeDiagnostics;

/// <summary>Carries a method start timestamp between generated Harmony prefix and postfix patches.</summary>
internal struct TimingPatchState
{
    internal TimingPatchState(long started, MethodTimingTarget target, int depth)
    {
        Started = started;
        Target = target;
        Depth = depth;
        Active = true;
    }

    internal long Started;
    internal MethodTimingTarget Target;
    internal int Depth;
    internal bool Active;
}

/// <summary>Tracks one nested timing call so inclusive and self time can be separated.</summary>
internal struct TimingStackFrame
{
    internal MethodTimingTarget Target;
    internal long ChildTicks;
}

/// <summary>Stores the active nested timing frames for one managed thread.</summary>
internal sealed class ThreadTimingStack
{
    internal readonly TimingStackFrame[] Frames = new TimingStackFrame[64];
    internal int Depth;
}

/// <summary>Aggregates call count and timing totals for one instrumented method.</summary>
internal sealed class MethodTimingTarget
{
    internal readonly long[] Histogram = new long[64];
    internal MethodBase Method;
    internal string DisplayName;
    internal string Signature;
    internal string SignatureHash;
    internal string IlHash;
    internal string ExistingOwners;
    internal string AssemblyName;
    internal string Category;
    internal long TotalCalls;
    internal long TotalTicks;
    internal long SelfTicks;
    internal long MainThreadCalls;
    internal long MainThreadTicks;
    internal long WorkerThreadCalls;
    internal long WorkerThreadTicks;
    internal long FrameTicks;
    internal long LastFrameTicks;
    internal long MaximumTicks;
    internal long LastSnapshotCalls;
    internal double LastSnapshotTime;
    internal double CallsPerSecond;

    internal void Record(long elapsed, long self, bool mainThread)
    {
        Interlocked.Increment(ref TotalCalls);
        Interlocked.Add(ref TotalTicks, elapsed);
        Interlocked.Add(ref SelfTicks, self);
        if (mainThread)
        {
            Interlocked.Increment(ref MainThreadCalls);
            Interlocked.Add(ref MainThreadTicks, elapsed);
        }
        else
        {
            Interlocked.Increment(ref WorkerThreadCalls);
            Interlocked.Add(ref WorkerThreadTicks, elapsed);
        }
        Interlocked.Add(ref FrameTicks, elapsed);
        long current;
        while (
            elapsed > (current = Interlocked.Read(ref MaximumTicks))
            && Interlocked.CompareExchange(ref MaximumTicks, elapsed, current) != current
        ) { }
        long micros = elapsed * 1000000L / Stopwatch.Frequency;
        int bucket = 0;
        while (micros > 1 && bucket < Histogram.Length - 1)
        {
            micros >>= 1;
            bucket++;
        }
        Interlocked.Increment(ref Histogram[bucket]);
    }

    internal double P95Milliseconds()
    {
        long calls = Interlocked.Read(ref TotalCalls);
        if (calls == 0)
        {
            return 0;
        }

        long threshold = (long)Math.Ceiling(calls * 0.95);
        long cumulative = 0;
        for (int i = 0; i < Histogram.Length; i++)
        {
            cumulative += Interlocked.Read(ref Histogram[i]);
            if (cumulative >= threshold)
            {
                return Math.Pow(2, i) / 1000.0;
            }
        }
        return 0;
    }
}

/// <summary>
/// Discovers selected hot methods and installs temporary Harmony instrumentation for diagnostic windows.
/// </summary>
internal sealed class MethodTimingFramework
{
    internal const string HarmonyId = "com.lucaswilluweit.tarkovperformancesuite.timing";
    private static readonly Dictionary<MethodBase, MethodTimingTarget> Lookup = new Dictionary<MethodBase, MethodTimingTarget>();
    private static volatile bool _active;
    private static int _patchFailures;
    private static volatile bool _circuitOpen;
    private static int _mainManagedThreadId;

    [ThreadStatic]
    private static ThreadTimingStack _threadStack;

    private readonly List<MethodTimingTarget> _targets = new List<MethodTimingTarget>(128);
    private readonly StringBuilder _overlayBuilder = new StringBuilder(1024);
    private readonly StringBuilder _reportBuilder = new StringBuilder(4096);
    private MethodTimingTarget[] _sortBuffer = new MethodTimingTarget[128];
    private readonly ManualLogSource _logger;
    private readonly Harmony _harmony = new Harmony(HarmonyId);
    private string _overlayText = string.Empty;
    private string _patchReport = string.Empty;
    private double _nextOverlayRefresh;
    private bool _initialized;
    private bool _circuitLogged;

    internal MethodTimingFramework(ManualLogSource logger)
    {
        _logger = logger;
    }

    internal string PatchReport
    {
        get { return _patchReport; }
    }

    internal bool Enabled
    {
        get { return _active && !_circuitOpen; }
    }

    internal void SetRuntimeEnabled(bool enabled)
    {
        _active = enabled && _initialized && _targets.Count > 0 && !_circuitOpen;
    }

    internal void Initialize(bool enabled)
    {
        if (!enabled || _initialized)
        {
            return;
        }

        _initialized = true;
        _mainManagedThreadId = Thread.CurrentThread.ManagedThreadId;
        Lookup.Clear();
        _targets.Clear();
        _reportBuilder.Clear();
        _reportBuilder.AppendLine("Harmony ID: " + HarmonyId);
        AddTarget(
            typeof(PlayerBody),
            "UpdatePlayerRenders",
            typeof(void),
            new[] { typeof(EPointOfView), typeof(EPlayerSide) },
            "EFT.PlayerBody.UpdatePlayerRenders"
        );
        AddTarget(typeof(PlayerBody), "IsVisible", typeof(bool), Type.EmptyTypes, "EFT.PlayerBody.IsVisible");
        AddTarget(typeof(GameWorld), "Update", typeof(void), Type.EmptyTypes, "EFT.GameWorld.Update");
        AddTarget(typeof(GameWorldUnityTickListener), "Update", typeof(void), Type.EmptyTypes, "EFT.WorldTickListener.Update");
        AddTarget(typeof(GameWorldUnityTickListener), "FixedUpdate", typeof(void), Type.EmptyTypes, "EFT.WorldTickListener.FixedUpdate");
        AddTarget(typeof(GameWorldUnityTickListener), "LateUpdate", typeof(void), Type.EmptyTypes, "EFT.WorldTickListener.LateUpdate");
        AddTarget(typeof(GameWorld), "DoWorldTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.DoWorldTick");
        AddTarget(typeof(GameWorld), "DoOtherWorldTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.DoOtherWorldTick");
        AddTarget(typeof(GameWorld), "BeforeWorldTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.BeforeWorldTick");
        AddTarget(typeof(GameWorld), "PlayerTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.PlayerTick");
        AddTarget(typeof(GameWorld), "BallisticsTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.BallisticsTick");
        AddTarget(typeof(GameWorld), "AfterPlayerTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.AfterPlayerTick");
        AddTarget(typeof(GameWorld), "OtherElseWorldTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.OtherElseWorldTick");
        AddTarget(typeof(GameWorld), "AfterWorldTick", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.AfterWorldTick");
        AddTarget(typeof(GameWorld), "LateUpdateWorld", typeof(void), new[] { typeof(float) }, "EFT.GameWorld.LateUpdateWorld");
        AddTarget(typeof(Player), "UpdateTick", typeof(void), Type.EmptyTypes, "EFT.Player.UpdateTick");
        AddTarget(typeof(Player), "FixedUpdateTick", typeof(void), Type.EmptyTypes, "EFT.Player.FixedUpdateTick");
        AddTarget(typeof(Player), "AfterMainTick", typeof(void), Type.EmptyTypes, "EFT.Player.AfterMainTick");
        AddTarget(typeof(Player), "LateUpdate", typeof(void), Type.EmptyTypes, "EFT.Player.LateUpdate");
        AddTarget(typeof(Player), "VisualPass", typeof(void), Type.EmptyTypes, "EFT.Player.VisualPass");
        AddTarget(typeof(Player), "ComplexUpdate", typeof(void), new[] { typeof(EUpdateQueue), typeof(float) }, "EFT.Player.ComplexUpdate");
        AddTarget(
            typeof(Player),
            "ComplexLateUpdate",
            typeof(void),
            new[] { typeof(EUpdateQueue), typeof(float) },
            "EFT.Player.ComplexLateUpdate"
        );
        AddTarget(typeof(Player), "ArmsUpdate", typeof(void), new[] { typeof(float) }, "EFT.Player.ArmsUpdate");
        AddTarget(typeof(Player), "BodyUpdate", typeof(void), new[] { typeof(float), typeof(int) }, "EFT.Player.BodyUpdate");
        AddTarget(
            typeof(Player),
            "ManualUpdate",
            typeof(void),
            new[] { typeof(float), typeof(float?), typeof(int) },
            "EFT.Player.ManualUpdate"
        );
        AddTarget(typeof(Player), "FBBIKUpdate", typeof(void), new[] { typeof(float) }, "EFT.Player.FBBIKUpdate");
        AddTarget(typeof(Player), "PropUpdate", typeof(void), Type.EmptyTypes, "EFT.Player.PropUpdate");
        AddTarget(typeof(BasePhysicalClass), "LateUpdate", typeof(void), Type.EmptyTypes, "EFT.BasePhysicalClass.LateUpdate");
        AddWorldPresentationTargets();
        AddOptionalFikaTargets();
        AddOptionalAiPluginTargets();
        AddAudioTargets();
        AddDiscoveredEftSystemTargets();
        AddInstalledPluginFrameTargets();
        _active = _targets.Count > 0;
        _patchReport = _reportBuilder.ToString();
        _logger.LogInfo($"Method timing enabled: {_targets.Count} verified targets patched. No invocation-level logging is performed.");
    }

    internal void FrameBoundary(double now)
    {
        if (!_active)
        {
            return;
        }

        for (int i = 0; i < _targets.Count; i++)
        {
            _targets[i].LastFrameTicks = Interlocked.Exchange(ref _targets[i].FrameTicks, 0);
        }

        if (_circuitOpen && !_circuitLogged)
        {
            _circuitLogged = true;
            _logger.LogError("Method timing circuit breaker opened after three patch-side exceptions; original methods continue normally.");
        }
    }

    internal string GetOverlayText(double now)
    {
        if (!_active)
        {
            return "Method timing: disabled";
        }

        if (_circuitOpen)
        {
            return "Method timing: circuit breaker open";
        }

        if (now < _nextOverlayRefresh)
        {
            return _overlayText;
        }

        _nextOverlayRefresh = now + 0.5;
        _overlayBuilder.Clear();
        _overlayBuilder.AppendLine("Top instrumented methods:");
        if (_sortBuffer.Length < _targets.Count)
        {
            Array.Resize(ref _sortBuffer, Math.Max(_targets.Count, _sortBuffer.Length * 2));
        }

        int targetCount = _targets.Count;
        for (int i = 0; i < _targets.Count; i++)
        {
            MethodTimingTarget target = _targets[i];
            long calls = Interlocked.Read(ref target.TotalCalls);
            long deltaCalls = calls - target.LastSnapshotCalls;
            double deltaTime = now - target.LastSnapshotTime;
            if (deltaTime > 0)
            {
                target.CallsPerSecond = deltaCalls / deltaTime;
            }

            target.LastSnapshotCalls = calls;
            target.LastSnapshotTime = now;
            if (i < targetCount)
            {
                _sortBuffer[i] = target;
            }
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
            _overlayBuilder
                .Append("  ")
                .Append(target.DisplayName)
                .Append(": ")
                .Append(target.CallsPerSecond.ToString("F1"))
                .Append("/s | frame ")
                .Append(frameMs.ToString("F3"))
                .Append(" ms | avg ")
                .Append(averageMs.ToString("F4"))
                .Append(" | max ")
                .Append(maximumMs.ToString("F3"))
                .Append(" | p95~ ")
                .Append(target.P95Milliseconds().ToString("F3"))
                .AppendLine(" ms");
        }
        _overlayText = _overlayBuilder.ToString();
        return _overlayText;
    }

    internal string GetDiagnosticSnapshot(double now)
    {
        return BuildCumulativeReport(now, 0, "current-session", null);
    }

    internal string WriteCumulativeReport(string directory, string map, double captureSeconds, string processThreadReport)
    {
        Directory.CreateDirectory(directory);
        string safeMap = Sanitize(map);
        string path = Path.Combine(directory, "cpu-profile_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + safeMap + ".txt");
        string report = BuildCumulativeReport(0, captureSeconds, map, processThreadReport);
        File.WriteAllText(path, report, new UTF8Encoding(false));
        return path;
    }

    internal void ResetAggregates(double now)
    {
        for (int i = 0; i < _targets.Count; i++)
        {
            MethodTimingTarget target = _targets[i];
            Interlocked.Exchange(ref target.TotalCalls, 0);
            Interlocked.Exchange(ref target.TotalTicks, 0);
            Interlocked.Exchange(ref target.SelfTicks, 0);
            Interlocked.Exchange(ref target.MainThreadCalls, 0);
            Interlocked.Exchange(ref target.MainThreadTicks, 0);
            Interlocked.Exchange(ref target.WorkerThreadCalls, 0);
            Interlocked.Exchange(ref target.WorkerThreadTicks, 0);
            Interlocked.Exchange(ref target.FrameTicks, 0);
            target.LastFrameTicks = 0;
            Interlocked.Exchange(ref target.MaximumTicks, 0);
            for (int bucket = 0; bucket < target.Histogram.Length; bucket++)
            {
                Interlocked.Exchange(ref target.Histogram[bucket], 0);
            }

            target.LastSnapshotCalls = 0;
            target.LastSnapshotTime = now;
            target.CallsPerSecond = 0;
        }
        _overlayText = string.Empty;
        _nextOverlayRefresh = 0;
        _threadStack = null;
    }

    internal void Shutdown()
    {
        _active = false;
        if (_initialized)
        {
            _harmony.UnpatchSelf();
        }

        Lookup.Clear();
        _targets.Clear();
        _initialized = false;
    }

    private void AddOptionalFikaTargets()
    {
        Assembly fika = FindAssembly("Fika.Core");
        if (fika == null)
        {
            _reportBuilder.AppendLine("Fika.Core: not loaded; optional targets not patched.");
            return;
        }
        Type client = fika.GetType("Fika.Core.Networking.FikaClient", false);
        Type observed = fika.GetType("Fika.Core.Main.Players.ObservedPlayer", false);
        Type fikaPlayer = fika.GetType("Fika.Core.Main.Players.FikaPlayer", false);
        Type observedFirearm = fika.GetType("Fika.Core.Main.ObservedClasses.HandsControllers.ObservedFirearmController", false);
        AddTarget(client, "Update", typeof(void), Type.EmptyTypes, "FikaClient.Update");
        AddTarget(observed, "ManualStateUpdate", typeof(void), new[] { typeof(double) }, "ObservedPlayer.ManualStateUpdate");
        AddTarget(observed, "LateUpdate", typeof(void), Type.EmptyTypes, "ObservedPlayer.LateUpdate");
        AddTarget(
            observed,
            "ManualUpdate",
            typeof(void),
            new[] { typeof(float), typeof(float?), typeof(int) },
            "ObservedPlayer.ManualUpdate"
        );
        AddTarget(observed, "ObservedVisualPass", typeof(void), new[] { typeof(float), typeof(int) }, "ObservedPlayer.ObservedVisualPass");
        AddTarget(
            observed,
            "ObservedFBBIKUpdate",
            typeof(void),
            new[] { typeof(float), typeof(int) },
            "ObservedPlayer.ObservedFBBIKUpdate"
        );
        AddTarget(
            fikaPlayer,
            "ManualUpdate",
            typeof(void),
            new[] { typeof(float), typeof(float?), typeof(int) },
            "FikaPlayer.ManualUpdate"
        );
        if (observedFirearm != null)
        {
            MethodInfo shotHandler = observedFirearm.GetMethod(
                "HandleShotInfoPacket",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (shotHandler != null)
            {
                AddTarget(shotHandler, "Fika.ObservedFirearm.HandleShotInfoPacket");
            }
            else
            {
                _reportBuilder.AppendLine("Fika.ObservedFirearm.HandleShotInfoPacket: expected method not found; skipped.");
            }
        }
    }

    private void AddOptionalAiPluginTargets()
    {
        Assembly orbit = FindAssembly("ORBIT");
        if (orbit == null)
        {
            _reportBuilder.AppendLine("ORBIT: not loaded; authoritative AI targets not patched on this process.");
        }
        else
        {
            AddFirstNamedTarget(orbit, "Orbit.Core.OrbitManager", "Update", "ORBIT.Manager.Update");
            AddFirstNamedTarget(orbit, "Orbit.Core.StrategyManager", "Update", "ORBIT.Strategy.Update");
            AddFirstNamedTarget(orbit, "Orbit.Core.ActionManager", "Update", "ORBIT.Action.Update");
            AddFirstNamedTarget(orbit, "Orbit.Systems.MovementSystem", "Update", "ORBIT.Movement.Update");
            AddFirstNamedTarget(orbit, "Orbit.Systems.LookSystem", "Update", "ORBIT.Look.Update");
            AddFirstNamedTarget(orbit, "Orbit.Navigation.NavJobExecutor", "Update", "ORBIT.Navigation.CalculatePaths");
        }

        Assembly sain = FindAssembly("SAIN");
        if (sain == null)
        {
            _reportBuilder.AppendLine("SAIN: not loaded; authoritative AI targets not patched on this process.");
        }
        else
        {
            AddFirstNamedTarget(sain, "SAIN.Components.BotManagerComponent", "ManualUpdate", "SAIN.BotManager.ManualUpdate");
            AddFirstNamedTarget(sain, "SAIN.Components.BotComponent", "ManualUpdate", "SAIN.Bot.ManualUpdate");
            AddFirstNamedTarget(
                sain,
                "SAIN.Components.BotController.BotSpawnController",
                "ManualUpdate",
                "SAIN.SpawnController.ManualUpdate"
            );
            AddFirstNamedTarget(sain, "SAIN.SAINComponent.Classes.Decision.BotDecisionManager", "Update", "SAIN.Decision.Update");
            AddFirstNamedTarget(sain, "SAIN.SAINComponent.Classes.SAINVisionClass", "Update", "SAIN.Vision.Update");
            AddFirstNamedTarget(sain, "SAIN.SAINComponent.Classes.SAINHearingSensorClass", "Update", "SAIN.Hearing.Update");
        }

        Assembly bigBrain = FindAssembly("DrakiaXYZ-BigBrain");
        if (bigBrain == null)
        {
            _reportBuilder.AppendLine("BigBrain: not loaded; brain targets not patched on this process.");
        }
        else
        {
            AddFirstNamedTarget(bigBrain, "DrakiaXYZ.BigBrain.Patches.BotAgentUpdatePatch", "PatchPrefix", "BigBrain.Agent.Update");
            AddFirstNamedTarget(bigBrain, "DrakiaXYZ.BigBrain.Patches.BotBaseBrainUpdatePatch", "PatchPrefix", "BigBrain.Layer.Update");
        }
    }

    private void AddFirstNamedTarget(Assembly assembly, string typeName, string methodName, string displayName)
    {
        Type type = assembly?.GetType(typeName, false);
        if (type == null)
        {
            _reportBuilder.AppendLine(displayName + ": type not found; skipped.");
            return;
        }
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.IsAbstract || method.ContainsGenericParameters)
            {
                continue;
            }

            AddTarget(method, displayName);
            return;
        }
        _reportBuilder.AppendLine(displayName + ": method not found; skipped.");
    }

    private void AddWorldPresentationTargets()
    {
        Assembly game = typeof(Player).Assembly;
        AddTarget(
            typeof(Shell),
            "ActivatePhysics",
            typeof(void),
            new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Vector3) },
            "EFT.Shell.ActivatePhysics"
        );
        AddTarget(typeof(Shell), "Update", typeof(void), Type.EmptyTypes, "EFT.Shell.Update");
        AddTarget(
            typeof(Player.FirearmController),
            nameof(Player.FirearmController.InitiateShot),
            typeof(void),
            new[] { typeof(IWeapon), typeof(AmmoItemClass), typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(int), typeof(float) },
            "Combat.FirearmController.InitiateShot"
        );
        AddTarget(
            typeof(WeaponManagerClass),
            nameof(WeaponManagerClass.PlayShotEffects),
            typeof(void),
            new[] { typeof(bool), typeof(float) },
            "Combat.WeaponManager.PlayShotEffects"
        );
        AddTarget(
            typeof(EffectsCommutator),
            nameof(EffectsCommutator.PlayHitEffect),
            typeof(void),
            new[] { typeof(EftBulletClass), typeof(ShotInfoClass) },
            "Combat.Effects.PlayHitEffect"
        );
        AddTarget(
            game.GetType("BulletSoundPlayersController", false),
            "Update",
            typeof(void),
            Type.EmptyTypes,
            "EFT.BulletSoundPlayersController.Update"
        );
        AddTarget(game.GetType("CullingManager", false), "Update", typeof(void), Type.EmptyTypes, "EFT.CullingManager.Update");
        AddTarget(game.GetType("DistantShadow", false), "Update", typeof(void), Type.EmptyTypes, "EFT.DistantShadow.Update");
        AddTarget(game.GetType("EffectsController", false), "Update", typeof(void), Type.EmptyTypes, "EFT.EffectsController.Update");
        AddTarget(game.GetType("Diz.Jobs.JobScheduler", false), "LateUpdate", typeof(void), Type.EmptyTypes, "EFT.JobScheduler.LateUpdate");
        AddTarget(
            typeof(AreaLight),
            nameof(AreaLight.SetUpCommandBuffer),
            typeof(void),
            new[] { typeof(Camera) },
            "Lighting.AreaLight.BuildCommandBuffer"
        );
        _reportBuilder.AppendLine(
            "Lighting.AreaLight.PreCull/ManualUpdate: intentionally not invocation-timed; the previous profile measured over 150,000 calls/s and patch overhead distorted FPS. BuildCommandBuffer remains timed."
        );
        AddTarget(game.GetType("AmbientLight", false), "Update", typeof(void), Type.EmptyTypes, "Lighting.AmbientLight.Update");
        AddTarget(
            game.GetType("AmbientLight", false),
            "ManualOnRenderObject",
            typeof(void),
            new[] { typeof(Camera) },
            "Lighting.AmbientLight.RenderObject"
        );
        AddTarget(
            typeof(ObservedCullingManager),
            nameof(ObservedCullingManager.Update),
            typeof(void),
            Type.EmptyTypes,
            "Culling.ObservedPlayers.Update"
        );
        AddTarget(typeof(ObservedCullingManager), "method_3", typeof(void), Type.EmptyTypes, "Culling.ObservedPlayers.CompleteJob");
        AddTarget(typeof(GClass1237), nameof(GClass1237.Update), typeof(void), Type.EmptyTypes, "Culling.PerfectGrid.Update");
        AddTarget(typeof(GClass1237), nameof(GClass1237.LateUpdate), typeof(void), Type.EmptyTypes, "Culling.PerfectGrid.LateUpdate");
        AddTarget(
            game.GetType("Koenigz.PerfectCulling.PerfectCullingCamera", false),
            "Update",
            typeof(void),
            Type.EmptyTypes,
            "Culling.PerfectCamera.Update"
        );
    }

    private void AddAudioTargets()
    {
        Assembly game = typeof(Player).Assembly;
        AddTarget(game.GetType("BetterAudio", false), "Update", typeof(void), Type.EmptyTypes, "Audio.BetterAudio.Update");
        AddTarget(
            game.GetType("Audio.SpatialSystem.SpatialAudioSystem", false),
            "Update",
            typeof(void),
            Type.EmptyTypes,
            "Audio.SpatialAudioSystem.Update"
        );
        AddTarget(
            game.GetType("Audio.SpatialSystem.SpatialAudioSystem", false),
            "LateUpdate",
            typeof(void),
            Type.EmptyTypes,
            "Audio.SpatialAudioSystem.LateUpdate"
        );
        AddTarget(
            game.GetType("Audio.AmbientSubsystem.AmbientAudioSystem", false),
            "Update",
            typeof(void),
            Type.EmptyTypes,
            "Audio.AmbientAudioSystem.Update"
        );
        AddTarget(
            game.GetType("Audio.AmbientSubsystem.AmbientAudioSystem", false),
            "LateUpdate",
            typeof(void),
            Type.EmptyTypes,
            "Audio.AmbientAudioSystem.LateUpdate"
        );
        AddTarget(game.GetType("BaseSoundPlayer", false), "Update", typeof(void), Type.EmptyTypes, "Audio.BaseSoundPlayer.Update");
    }

    private void AddInstalledPluginFrameTargets()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        string[] frameMethods = { "Update", "LateUpdate", "FixedUpdate", "OnGUI" };
        int found = 0;
        const int maximumDiscoveredTargets = 192;
        var scannedAssemblies = new HashSet<Assembly>();
        foreach (KeyValuePair<string, BepInEx.PluginInfo> pair in Chainloader.PluginInfos)
        {
            BepInEx.PluginInfo info = pair.Value;
            object instance = info?.Instance;
            if (instance == null || instance is Plugin)
            {
                continue;
            }

            Assembly assembly = instance.GetType().Assembly;
            if (!scannedAssemblies.Add(assembly))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch (Exception ex)
            {
                _reportBuilder.AppendLine(
                    "Mod[" + (info.Metadata?.GUID ?? assembly.GetName().Name) + "]: type discovery skipped; " + ex.Message
                );
                continue;
            }

            string guid = info.Metadata?.GUID ?? assembly.GetName().Name;
            for (int typeIndex = 0; typeIndex < types.Length && found < maximumDiscoveredTargets; typeIndex++)
            {
                Type type = types[typeIndex];
                if (type == null || type.Assembly == typeof(Plugin).Assembly || !typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    continue;
                }

                for (int i = 0; i < frameMethods.Length && found < maximumDiscoveredTargets; i++)
                {
                    MethodInfo method = type.GetMethod(frameMethods[i], flags, null, Type.EmptyTypes, null);
                    if (method == null || method.ReturnType != typeof(void) || method.IsAbstract || method.ContainsGenericParameters)
                    {
                        continue;
                    }

                    AddTarget(method, "Mod[" + guid + "]." + type.FullName + "." + frameMethods[i]);
                    found++;
                }
            }
        }
        _reportBuilder.AppendLine(
            "Installed-plugin MonoBehaviour frame methods discovered: "
                + found
                + " (safety cap "
                + maximumDiscoveredTargets
                + "). Inclusive nested timings must not be added together; use self time for attribution."
        );
    }

    private void AddDiscoveredEftSystemTargets()
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const int maximumTargets = 128;
        string[] frameMethods = { "Update", "LateUpdate", "FixedUpdate", "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderObject" };
        string[] highValueNames =
        {
            "Audio",
            "Culling",
            "Effect",
            "Ballistic",
            "JobScheduler",
            "DistantShadow",
            "AmbientLight",
            "AreaLight",
            "Decal",
            "Bullet",
            "Shell",
            "Weather",
            "GPUInstancer",
            "Occlusion",
            "Streaming",
            "GameWorld",
        };
        Type[] types;
        try
        {
            types = typeof(Player).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch (Exception ex)
        {
            _reportBuilder.AppendLine("Automatic EFT system discovery skipped: " + ex.Message);
            return;
        }

        int found = 0;
        for (int typeIndex = 0; typeIndex < types.Length && found < maximumTargets; typeIndex++)
        {
            Type type = types[typeIndex];
            if (type == null || type.IsAbstract || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                continue;
            }

            string fullName = type.FullName ?? type.Name;
            if (fullName.IndexOf("PerfectCullingCrossSceneGroup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _reportBuilder.AppendLine(
                    "AutoEFT."
                        + fullName
                        + ": intentionally omitted from invocation timing because it runs tens of thousands of times per second."
                );
                continue;
            }
            bool relevant = false;
            for (int nameIndex = 0; nameIndex < highValueNames.Length; nameIndex++)
            {
                if (fullName.IndexOf(highValueNames[nameIndex], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                relevant = true;
                break;
            }
            if (!relevant)
            {
                continue;
            }

            for (int methodIndex = 0; methodIndex < frameMethods.Length && found < maximumTargets; methodIndex++)
            {
                MethodInfo method = type.GetMethod(frameMethods[methodIndex], flags, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(void) || method.IsAbstract || method.ContainsGenericParameters)
                {
                    continue;
                }

                AddTarget(method, "AutoEFT." + fullName + "." + frameMethods[methodIndex]);
                found++;
            }
        }
        _reportBuilder.AppendLine(
            "Automatically discovered high-value EFT frame/render callbacks: " + found + " (safety cap " + maximumTargets + ")."
        );
    }

    private void AddTarget(Type type, string methodName, Type expectedReturn, Type[] parameters, string displayName)
    {
        if (type == null)
        {
            _reportBuilder.AppendLine(displayName + ": type not found; skipped.");
            return;
        }
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            parameters,
            null
        );
        if (method == null || method.ReturnType != expectedReturn)
        {
            _reportBuilder.AppendLine(displayName + ": expected signature not found; skipped.");
            return;
        }

        AddTarget(method, displayName);
    }

    private void AddTarget(MethodInfo method, string displayName)
    {
        if (Lookup.ContainsKey(method))
        {
            _reportBuilder.AppendLine(displayName + ": duplicate method target; skipped.");
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
                ExistingOwners = owners,
                AssemblyName = method.Module.Assembly.GetName().Name,
                Category = CategoryFor(displayName),
            };
            var prefix = new HarmonyMethod(
                typeof(MethodTimingFramework).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)
            );
            var postfix = new HarmonyMethod(
                typeof(MethodTimingFramework).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)
            );
            var finalizer = new HarmonyMethod(
                typeof(MethodTimingFramework).GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic)
            );
            _harmony.Patch(method, prefix, postfix, null, finalizer, null);
            Lookup.Add(method, target);
            _targets.Add(target);
            string access =
                method.IsPublic ? "public"
                : method.IsPrivate ? "private"
                : method.IsFamily ? "protected"
                : "internal";
            _reportBuilder.AppendLine(
                $"{displayName} | {method.Module.Assembly.GetName().Name} | {target.Signature} | {access} | virtual={method.IsVirtual} | signatureSHA256={target.SignatureHash} | ilSHA256={target.IlHash} | existingOwners={owners}"
            );
            _logger.LogInfo(
                $"Timing patch target verified: {target.Signature}; assembly={method.Module.Assembly.FullName}; existing owners={owners}"
            );
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
        if (!_active || _circuitOpen)
        {
            return;
        }

        try
        {
            if (!Lookup.TryGetValue(__originalMethod, out MethodTimingTarget target))
            {
                return;
            }

            ThreadTimingStack stack = _threadStack ?? (_threadStack = new ThreadTimingStack());
            if (stack.Depth >= stack.Frames.Length)
            {
                return;
            }

            int depth = stack.Depth++;
            stack.Frames[depth].Target = target;
            stack.Frames[depth].ChildTicks = 0;
            __state = new TimingPatchState(Stopwatch.GetTimestamp(), target, depth);
        }
        catch
        {
            PatchFailure();
        }
    }

    private static void Postfix(ref TimingPatchState __state)
    {
        CompleteTiming(ref __state);
    }

    private static Exception Finalizer(Exception __exception, ref TimingPatchState __state)
    {
        CompleteTiming(ref __state);
        return __exception;
    }

    private static void CompleteTiming(ref TimingPatchState state)
    {
        if (!state.Active || state.Target == null || _circuitOpen)
        {
            return;
        }

        state.Active = false;
        try
        {
            long elapsed = Stopwatch.GetTimestamp() - state.Started;
            ThreadTimingStack stack = _threadStack;
            long childTicks = 0;
            if (stack != null && state.Depth >= 0 && state.Depth < stack.Frames.Length)
            {
                childTicks = stack.Frames[state.Depth].ChildTicks;
                stack.Frames[state.Depth] = default;
                stack.Depth = state.Depth;
                if (state.Depth > 0)
                {
                    stack.Frames[state.Depth - 1].ChildTicks += elapsed;
                }
            }
            long self = elapsed > childTicks ? elapsed - childTicks : 0;
            state.Target.Record(elapsed, self, Thread.CurrentThread.ManagedThreadId == _mainManagedThreadId);
        }
        catch
        {
            PatchFailure();
        }
    }

    private string BuildCumulativeReport(double now, double captureSeconds, string map, string processThreadReport)
    {
        var activeTargets = new List<MethodTimingTarget>(_targets.Count);
        var categories = new Dictionary<string, CategoryTiming>(StringComparer.OrdinalIgnoreCase);
        long measuredSelfTicks = 0;
        long measuredMainTicks = 0;
        long measuredWorkerTicks = 0;
        long calls = 0;
        for (int i = 0; i < _targets.Count; i++)
        {
            MethodTimingTarget target = _targets[i];
            long targetCalls = Interlocked.Read(ref target.TotalCalls);
            if (targetCalls == 0)
            {
                continue;
            }

            activeTargets.Add(target);
            long selfTicks = Interlocked.Read(ref target.SelfTicks);
            long mainTicks = Interlocked.Read(ref target.MainThreadTicks);
            long workerTicks = Interlocked.Read(ref target.WorkerThreadTicks);
            measuredSelfTicks += selfTicks;
            measuredMainTicks += mainTicks;
            measuredWorkerTicks += workerTicks;
            calls += targetCalls;
            if (!categories.TryGetValue(target.Category, out CategoryTiming category))
            {
                category = new CategoryTiming(target.Category);
                categories.Add(target.Category, category);
            }
            category.SelfTicks += selfTicks;
            category.InclusiveTicks += Interlocked.Read(ref target.TotalTicks);
            category.Calls += targetCalls;
            category.MainTicks += mainTicks;
            category.WorkerTicks += workerTicks;
        }

        activeTargets.Sort((left, right) => Interlocked.Read(ref right.SelfTicks).CompareTo(Interlocked.Read(ref left.SelfTicks)));
        var categoryList = new List<CategoryTiming>(categories.Values);
        categoryList.Sort((left, right) => right.SelfTicks.CompareTo(left.SelfTicks));
        double duration = captureSeconds > 0 ? captureSeconds : Math.Max(0.001, now);
        double durationMs = duration * 1000.0;
        double measuredSelfMs = TicksToMilliseconds(measuredSelfTicks);
        double mainMs = TicksToMilliseconds(measuredMainTicks);
        double workerMs = TicksToMilliseconds(measuredWorkerTicks);

        var builder = new StringBuilder(Math.Max(8192, activeTargets.Count * 180));
        builder.AppendLine("Tarkov Performance Suite cumulative CPU profile");
        builder.AppendLine("Version: " + Plugin.PluginVersion);
        builder.AppendLine("Map: " + (string.IsNullOrWhiteSpace(map) ? "unknown" : map));
        builder.AppendLine("Capture: " + duration.ToString("F2", CultureInfo.InvariantCulture) + " seconds");
        builder.AppendLine(
            "Patched methods: " + _targets.Count + " | methods called: " + activeTargets.Count + " | measured calls: " + calls
        );
        builder.AppendLine(
            "Instrumented self CPU: "
                + measuredSelfMs.ToString("F2", CultureInfo.InvariantCulture)
                + " ms ("
                + Percent(measuredSelfMs, durationMs)
                + "% of one CPU core over the capture)"
        );
        builder.AppendLine(
            "Instrumented inclusive CPU: main thread "
                + mainMs.ToString("F2", CultureInfo.InvariantCulture)
                + " ms | worker threads "
                + workerMs.ToString("F2", CultureInfo.InvariantCulture)
                + " ms"
        );
        builder.AppendLine();
        builder.AppendLine("How to read this");
        builder.AppendLine("- Self ms excludes time spent inside other instrumented methods and is the best ranking for attribution.");
        builder.AppendLine("- Inclusive ms includes instrumented child calls. Do not add inclusive rows together.");
        builder.AppendLine(
            "- Main/worker columns show where calls executed. Native Unity, rendering-driver and uninstrumented EFT time appears in the frame/process sections, not as a managed method."
        );
        builder.AppendLine();
        builder.AppendLine("Top categories by cumulative self time");
        builder.AppendLine("rank | category | self ms | inclusive ms | calls | main ms | worker ms | one-core %");
        for (int i = 0; i < categoryList.Count; i++)
        {
            CategoryTiming category = categoryList[i];
            double selfMs = TicksToMilliseconds(category.SelfTicks);
            builder
                .Append(i + 1)
                .Append(" | ")
                .Append(category.Name)
                .Append(" | ")
                .Append(selfMs.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(TicksToMilliseconds(category.InclusiveTicks).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(category.Calls)
                .Append(" | ")
                .Append(TicksToMilliseconds(category.MainTicks).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(TicksToMilliseconds(category.WorkerTicks).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(Percent(selfMs, durationMs))
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("All instrumented methods ranked by cumulative self time");
        builder.AppendLine(
            "rank | method | assembly | self ms | inclusive ms | calls | avg ms | p95~ ms | max ms | main ms | worker ms | one-core %"
        );
        for (int i = 0; i < activeTargets.Count; i++)
        {
            MethodTimingTarget target = activeTargets[i];
            long targetCalls = Interlocked.Read(ref target.TotalCalls);
            long inclusiveTicks = Interlocked.Read(ref target.TotalTicks);
            double selfMs = TicksToMilliseconds(Interlocked.Read(ref target.SelfTicks));
            double inclusiveMs = TicksToMilliseconds(inclusiveTicks);
            builder
                .Append(i + 1)
                .Append(" | ")
                .Append(target.DisplayName)
                .Append(" | ")
                .Append(target.AssemblyName)
                .Append(" | ")
                .Append(selfMs.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(inclusiveMs.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(targetCalls)
                .Append(" | ")
                .Append((targetCalls > 0 ? inclusiveMs / targetCalls : 0).ToString("F5", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(target.P95Milliseconds().ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(TicksToMilliseconds(Interlocked.Read(ref target.MaximumTicks)).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(TicksToMilliseconds(Interlocked.Read(ref target.MainThreadTicks)).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(TicksToMilliseconds(Interlocked.Read(ref target.WorkerThreadTicks)).ToString("F3", CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(Percent(selfMs, durationMs))
                .AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(processThreadReport))
        {
            builder.AppendLine();
            builder.AppendLine(processThreadReport.TrimEnd());
        }
        return builder.ToString();
    }

    private static string CategoryFor(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Other";
        }

        if (displayName.StartsWith("Mod[", StringComparison.Ordinal))
        {
            int end = displayName.IndexOf(']');
            return end > 4 ? displayName.Substring(0, end + 1) : "Mods";
        }
        int separator = displayName.IndexOf('.');
        return separator > 0 ? displayName.Substring(0, separator) : "Other";
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static string Percent(double value, double total)
    {
        return (total > 0 ? value * 100.0 / total : 0).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown-map";
        }

        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_')
            {
                chars[i] = '-';
            }
        }

        return new string(chars);
    }

    /// <summary>Aggregates timings when individual method cardinality is intentionally capped.</summary>
    private sealed class CategoryTiming
    {
        internal CategoryTiming(string name)
        {
            Name = name;
        }

        internal string Name { get; }
        internal long SelfTicks;
        internal long InclusiveTicks;
        internal long Calls;
        internal long MainTicks;
        internal long WorkerTicks;
    }

    private static void PatchFailure()
    {
        if (Interlocked.Increment(ref _patchFailures) >= 3)
        {
            _circuitOpen = true;
            _active = false;
        }
    }

    private static Assembly FindAssembly(string name)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            if (string.Equals(assemblies[i].GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return assemblies[i];
            }
        }

        return null;
    }

    private static string HashIl(MethodInfo method)
    {
        try
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null)
            {
                return "unavailable";
            }

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }
        catch
        {
            return "unavailable";
        }
    }
}
