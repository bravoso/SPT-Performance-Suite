using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.CameraControl;
using EFT.InventoryLogic;
using HarmonyLib;
using Systems.Effects;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Exposes remote combat effects that were preserved, reduced, or suppressed.</summary>
internal readonly struct RemoteCombatCounters
{
    internal RemoteCombatCounters(
        long remoteShots,
        long soundOnlyShots,
        long safetyBypasses,
        long culledMuzzles,
        long culledImpacts,
        long culledShells,
        long skippedFlybyFrames,
        int culledLights,
        bool fikaClientWorld,
        double averageDecisionMs
    )
    {
        RemoteShots = remoteShots;
        SoundOnlyShots = soundOnlyShots;
        SafetyBypasses = safetyBypasses;
        CulledMuzzles = culledMuzzles;
        CulledImpacts = culledImpacts;
        CulledShells = culledShells;
        SkippedFlybyFrames = skippedFlybyFrames;
        CulledLights = culledLights;
        IsFikaClientWorld = fikaClientWorld;
        AverageDecisionMs = averageDecisionMs;
    }

    internal long RemoteShots { get; }
    internal long SoundOnlyShots { get; }
    internal long SafetyBypasses { get; }
    internal long CulledMuzzles { get; }
    internal long CulledImpacts { get; }
    internal long CulledShells { get; }
    internal long SkippedFlybyFrames { get; }
    internal int CulledLights { get; }
    internal bool IsFikaClientWorld { get; }
    internal double AverageDecisionMs { get; }
}

/// <summary>
/// Reduces distant visual effects after vanilla firearm and ballistic processing has run.
/// </summary>
/// <remarks>
/// The former sound-only shot replacement is retired. This feature never patches or suppresses
/// <c>FirearmController.InitiateShot</c>; it is limited to muzzle, impact, casing, light, and flyby presentation.
/// </remarks>
internal sealed class CombatPresentationBudgetFeature : IPerformanceFeature
{
    private const string FikaClientWorldName = "Fika.Core.Main.ClientClasses.FikaClientGameWorld";
    private static CombatPresentationBudgetFeature _instance;

    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly EntityRegistry _registry;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private readonly Dictionary<Light, int> _lightMasks = new Dictionary<Light, int>(64);
    private readonly List<Light> _lightRestoreBuffer = new List<Light>(32);

    private Harmony _harmony;
    private MethodInfo _playShotEffects;
    private MethodInfo _playHitEffect;
    private MethodInfo _shellActivate;
    private MethodInfo _bulletSoundUpdate;
    private bool _raidActive;
    private bool _isFikaClientWorld;
    private float _nextBulletSoundUpdate;
    private float _nextLightUpdate;
    private long _remoteShots;
    private long _soundOnlyShots;
    private long _safetyBypasses;
    private long _culledMuzzles;
    private long _culledImpacts;
    private long _culledDistantShells;
    private long _skippedBulletSoundFrames;
    private double _averageDecisionMs;

    internal CombatPresentationBudgetFeature(
        ManualLogSource logger,
        PluginConfiguration configuration,
        EntityRegistry registry,
        RecentExceptionLog exceptions
    )
    {
        _logger = logger;
        _configuration = configuration;
        _registry = registry;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "Remote Combat Relevance Firewall"; }
    }

    public bool IsAvailable
    {
        get
        {
            return _playShotEffects != null
                && _playHitEffect != null
                && _shellActivate != null
                && _bulletSoundUpdate != null
                && !_breaker.IsOpen;
        }
    }

    public bool IsEnabled { get; private set; }
    internal RemoteCombatCounters Counters
    {
        get
        {
            return new RemoteCombatCounters(
                _remoteShots,
                _soundOnlyShots,
                _safetyBypasses,
                _culledMuzzles,
                _culledImpacts,
                _culledDistantShells,
                _skippedBulletSoundFrames,
                _lightMasks.Count,
                _isFikaClientWorld,
                _averageDecisionMs
            );
        }
    }

    internal string StatusText
    {
        get
        {
            return IsEnabled
                    ? (_isFikaClientWorld ? "active (Fika client proxy presentation)" : "active (host/offline presentation)")
                        + " | shots "
                        + _remoteShots
                        + " sound-only "
                        + _soundOnlyShots
                        + " safety "
                        + _safetyBypasses
                        + " | muzzle "
                        + _culledMuzzles
                        + " impacts "
                        + _culledImpacts
                        + " casings "
                        + _culledDistantShells
                        + " lights "
                        + _lightMasks.Count
                        + " | decision "
                        + _averageDecisionMs.ToString("F4")
                        + " ms"
                : _breaker.IsOpen ? "disabled (circuit breaker)"
                : "disabled";
        }
    }

    public void Initialize()
    {
        _breaker.Reset();
        _instance = this;
        try
        {
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.combat-relevance");
            _playShotEffects = AccessTools.Method(
                typeof(WeaponManagerClass),
                nameof(WeaponManagerClass.PlayShotEffects),
                new[] { typeof(bool), typeof(float) }
            );
            _playHitEffect = AccessTools.Method(
                typeof(EffectsCommutator),
                nameof(EffectsCommutator.PlayHitEffect),
                new[] { typeof(EftBulletClass), typeof(ShotInfoClass) }
            );
            _shellActivate = AccessTools.Method(
                typeof(Shell),
                nameof(Shell.ActivatePhysics),
                new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Vector3) }
            );
            Type bulletSounds = typeof(Player).Assembly.GetType("BulletSoundPlayersController", false);
            _bulletSoundUpdate = AccessTools.Method(bulletSounds, "Update", Type.EmptyTypes);

            if (_playShotEffects == null || _playHitEffect == null || _shellActivate == null || _bulletSoundUpdate == null)
            {
                throw new MissingMethodException("One or more EFT combat relevance targets were not found.");
            }

            // Never suppress FirearmController.InitiateShot. Even on an observed Fika proxy, the method can
            // advance replicated weapon/animation state. Skipping it was correlated with multi-second firing
            // loops, so the sound-only replacement is retired and vanilla shot handling always runs.
            _configuration.SoundOnlyRemoteShots.Value = false;
            _harmony.Patch(
                _playShotEffects,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(PlayShotEffectsPrefix)))
            );
            _harmony.Patch(
                _playHitEffect,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(PlayHitEffectPrefix)))
            );
            _harmony.Patch(
                _shellActivate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(ShellActivatePrefix)))
            );
            _harmony.Patch(
                _bulletSoundUpdate,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(BulletSoundUpdatePrefix)))
            );
            _breaker.Success();
        }
        catch (Exception ex)
        {
            UnpatchAll();
            _exceptions.Add(Name + " patch install", ex);
            _logger.LogWarning(Name + " unavailable; vanilla combat remains unchanged: " + ex.Message);
        }
        SetEnabled(_configuration.CombatPresentationEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        _isFikaClientWorld = IsFikaClientWorld();
        _nextBulletSoundUpdate = 0f;
        _nextLightUpdate = 0f;
        _remoteShots = 0;
        _soundOnlyShots = 0;
        _safetyBypasses = 0;
        _culledMuzzles = 0;
        _culledImpacts = 0;
        _culledDistantShells = 0;
        _skippedBulletSoundFrames = 0;
        _averageDecisionMs = 0;
        RestoreLights();
        _logger.LogInfo(
            Name
                + " raid mode: "
                + (
                    _isFikaClientWorld
                        ? "non-host Fika client; vanilla firearm updates retained, visual-effect culling only."
                        : "host/offline; vanilla firearm updates retained, presentation culling only."
                )
        );
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        _isFikaClientWorld = false;
        _nextBulletSoundUpdate = 0f;
        _nextLightUpdate = 0f;
        RestoreLights();
    }

    internal void Tick(float now)
    {
        if (!IsEnabled || !_raidActive || !_configuration.CullHiddenRemoteLights.Value)
        {
            if (_lightMasks.Count != 0)
            {
                RestoreLights();
            }

            return;
        }
        if (now < _nextLightUpdate)
        {
            return;
        }

        _nextLightUpdate = now + 0.1f;

        try
        {
            float distance = Clamp(_configuration.HiddenRemoteLightDistance.Value, 30f, 250f);
            float distanceSquared = distance * distance;
            foreach (TrackedEntity entity in _registry.Entities)
            {
                bool hidden = IsRemote(entity) && entity.IsAlive && !entity.IsVisible && entity.DistanceSquared >= distanceSquared;
                Light[] lights = entity.Lights;
                for (int i = 0; i < lights.Length; i++)
                {
                    Light light = lights[i];
                    if (light == null)
                    {
                        continue;
                    }

                    if (hidden)
                    {
                        if (!_lightMasks.ContainsKey(light))
                        {
                            _lightMasks.Add(light, light.cullingMask);
                        }

                        if (light.cullingMask != 0)
                        {
                            light.cullingMask = 0;
                        }
                    }
                    else
                    {
                        RestoreLight(light);
                    }
                }
            }
            RemoveDestroyedLights();
            _breaker.Success();
        }
        catch (Exception ex)
        {
            FailOpen("remote lights", ex);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
        {
            return;
        }

        IsEnabled = enabled;
        _configuration.CombatPresentationEnabled.Value = enabled;
        if (!enabled)
        {
            _nextBulletSoundUpdate = 0f;
            RestoreLights();
        }
    }

    public void Shutdown()
    {
        _raidActive = false;
        IsEnabled = false;
        RestoreLights();
        UnpatchAll();
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private bool ShouldPlayMuzzle(WeaponManagerClass manager)
    {
        if (!IsEnabled || !_raidActive || !_configuration.CullDistantMuzzleEffects.Value || manager == null)
        {
            return true;
        }

        try
        {
            Player player = manager.Player;
            if (player == null || !_registry.TryGet(player, out TrackedEntity entity) || !IsRemote(entity))
            {
                return true;
            }

            float distance = Clamp(_configuration.DistantMuzzleEffectDistance.Value, 25f, 200f);
            if (entity.IsVisible || entity.DistanceSquared < distance * distance || IsPointVisible(player.Transform.position))
            {
                return true;
            }

            _culledMuzzles++;
            _breaker.Success();
            return false;
        }
        catch (Exception ex)
        {
            FailOpen("muzzle effects", ex);
            return true;
        }
    }

    private bool ShouldPlayHitEffect(EftBulletClass info)
    {
        if (!IsEnabled || !_raidActive || !_configuration.CullDistantImpactEffects.Value || info == null)
        {
            return true;
        }

        try
        {
            if (info.Player != null && info.Player.iPlayer != null && info.Player.iPlayer.IsYourPlayer)
            {
                return true;
            }

            if (!_registry.TryGetLocalPosition(out Vector3 listener))
            {
                return true;
            }

            float distance = Clamp(_configuration.DistantImpactEffectDistance.Value, 40f, 250f);
            if ((info.HitPoint - listener).sqrMagnitude < distance * distance || IsPointVisible(info.HitPoint))
            {
                return true;
            }

            if (info.Ammo is AmmoItemClass ammo && IsExplosiveOrSpecial(ammo))
            {
                return true;
            }

            _culledImpacts++;
            _breaker.Success();
            return false;
        }
        catch (Exception ex)
        {
            FailOpen("impact effects", ex);
            return true;
        }
    }

    private bool ShouldActivateShell(Shell shell, Vector3 beginPoint)
    {
        if (!IsEnabled || !_raidActive || !_configuration.CullDistantShellPhysics.Value || shell == null)
        {
            return true;
        }

        try
        {
            if (!CameraClass.Exist || CameraClass.Instance == null || CameraClass.Instance.Camera == null)
            {
                return true;
            }

            float distance = Clamp(_configuration.DistantShellPhysicsDistance.Value, 10f, 100f);
            if ((CameraClass.Instance.Camera.transform.position - beginPoint).sqrMagnitude <= distance * distance)
            {
                return true;
            }

            shell.transform.position = beginPoint;
            shell.Finished = true;
            shell.DisablePhysics();
            _culledDistantShells++;
            _breaker.Success();
            return false;
        }
        catch (Exception ex)
        {
            FailOpen("distant casing", ex);
            return true;
        }
    }

    private bool ShouldUpdateBulletSounds()
    {
        if (!IsEnabled || !_raidActive || !_configuration.BudgetBulletFlybyAudio.Value)
        {
            return true;
        }

        try
        {
            float now = Time.realtimeSinceStartup;
            int rate = Clamp(_configuration.BulletFlybyAudioRate.Value, 15, 120);
            if (now + 0.0001f < _nextBulletSoundUpdate)
            {
                _skippedBulletSoundFrames++;
                return false;
            }
            _nextBulletSoundUpdate = now + 1f / rate;
            _breaker.Success();
            return true;
        }
        catch (Exception ex)
        {
            FailOpen("bullet flyby audio", ex);
            return true;
        }
    }

    private static bool IsRemote(TrackedEntity entity)
    {
        return entity != null && (entity.Kind == EntityKind.RemoteAI || entity.Kind == EntityKind.RemoteHuman);
    }

    private static bool IsExplosiveOrSpecial(AmmoItemClass ammo)
    {
        if (ammo.AmmoTemplate.IsLightAndSoundShot || ammo.ProjectileCount != 1 || ammo.InitialSpeed <= 0f)
        {
            return true;
        }

        return ammo.GetItemComponent<ExplosiveItemComponentClass>() != null;
    }

    private static bool IsPointVisible(Vector3 point)
    {
        if (!CameraClass.Exist || CameraClass.Instance == null)
        {
            return true;
        }

        if (IsPointInCamera(CameraClass.Instance.Camera, point))
        {
            return true;
        }

        GClass3687 opticManager = CameraClass.Instance.OpticCameraManager;
        Camera optic = opticManager?.Camera;
        return optic != null && optic.enabled && optic.targetTexture != null && IsPointInCamera(optic, point);
    }

    private static bool IsPointInCamera(Camera camera, Vector3 point)
    {
        if (camera == null || !camera.enabled)
        {
            return false;
        }

        Vector3 viewport = camera.WorldToViewportPoint(point);
        return viewport.z > 0f && viewport.x >= -0.03f && viewport.x <= 1.03f && viewport.y >= -0.03f && viewport.y <= 1.03f;
    }

    private static bool IsFikaClientWorld()
    {
        if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
        {
            return false;
        }

        return string.Equals(Singleton<GameWorld>.Instance.GetType().FullName, FikaClientWorldName, StringComparison.Ordinal);
    }

    private void RestoreLight(Light light)
    {
        if (light == null || !_lightMasks.TryGetValue(light, out int mask))
        {
            return;
        }

        light.cullingMask = mask;
        _lightMasks.Remove(light);
    }

    private void RemoveDestroyedLights()
    {
        _lightRestoreBuffer.Clear();
        foreach (KeyValuePair<Light, int> pair in _lightMasks)
        {
            if (pair.Key == null)
            {
                _lightRestoreBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _lightRestoreBuffer.Count; i++)
        {
            _lightMasks.Remove(_lightRestoreBuffer[i]);
        }
    }

    private void RestoreLights()
    {
        foreach (KeyValuePair<Light, int> pair in _lightMasks)
        {
            try
            {
                if (pair.Key != null)
                {
                    pair.Key.cullingMask = pair.Value;
                }
            }
            catch { }
        }
        _lightMasks.Clear();
        _lightRestoreBuffer.Clear();
    }

    private void FailOpen(string operation, Exception ex)
    {
        _exceptions.Add(Name + " " + operation, ex);
        if (!_breaker.Failure())
        {
            return;
        }

        IsEnabled = false;
        _configuration.CombatPresentationEnabled.Value = false;
        RestoreLights();
        _logger.LogError(Name + " failed open and was disabled; vanilla behavior continues: " + ex);
    }

    private void UnpatchAll()
    {
        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
        }

        _playShotEffects = null;
        _playHitEffect = null;
        _shellActivate = null;
        _bulletSoundUpdate = null;
    }

    private static bool PlayShotEffectsPrefix(WeaponManagerClass __instance)
    {
        return _instance == null || _instance.ShouldPlayMuzzle(__instance);
    }

    private static bool PlayHitEffectPrefix(EftBulletClass info)
    {
        return _instance == null || _instance.ShouldPlayHitEffect(info);
    }

    private static bool ShellActivatePrefix(Shell __instance, Vector3 beginPoint)
    {
        return _instance == null || _instance.ShouldActivateShell(__instance, beginPoint);
    }

    private static bool BulletSoundUpdatePrefix()
    {
        return _instance == null || _instance.ShouldUpdateBulletSounds();
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return value < minimum ? minimum
            : value > maximum ? maximum
            : value;
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? minimum
            : value < minimum ? minimum
            : value > maximum ? maximum
            : value;
    }
}
