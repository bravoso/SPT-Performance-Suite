using System;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    /// <summary>
    /// Reduces client-only combat presentation work. Ballistics, damage, networking, weapon
    /// state, and nearby audio are never altered.
    /// </summary>
    internal sealed class CombatPresentationBudgetFeature : IPerformanceFeature
    {
        private static CombatPresentationBudgetFeature _instance;
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private Harmony _harmony;
        private MethodInfo _shellActivate;
        private MethodInfo _bulletSoundUpdate;
        private bool _raidActive;
        private float _nextBulletSoundUpdate;
        private long _culledDistantShells;
        private long _skippedBulletSoundFrames;

        internal CombatPresentationBudgetFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "Combat Presentation Budget";
        public bool IsAvailable => _shellActivate != null && _bulletSoundUpdate != null && !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal string StatusText => IsEnabled
            ? "enabled | distant casings " + _culledDistantShells + " | skipped flyby scans " + _skippedBulletSoundFrames
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            _instance = this;
            try
            {
                _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.combat-presentation");
                _shellActivate = AccessTools.Method(typeof(Shell), nameof(Shell.ActivatePhysics),
                    new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(Vector3) });
                Type bulletSounds = typeof(Player).Assembly.GetType("BulletSoundPlayersController", false);
                _bulletSoundUpdate = AccessTools.Method(bulletSounds, "Update", Type.EmptyTypes);
                MethodInfo shellPrefix = AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(ShellActivatePrefix));
                MethodInfo soundPrefix = AccessTools.Method(typeof(CombatPresentationBudgetFeature), nameof(BulletSoundUpdatePrefix));
                if (_shellActivate == null || _bulletSoundUpdate == null || shellPrefix == null || soundPrefix == null)
                    throw new MissingMethodException("Expected EFT shell or bullet-sound update method was not found.");
                _harmony.Patch(_shellActivate, prefix: new HarmonyMethod(shellPrefix));
                _harmony.Patch(_bulletSoundUpdate, prefix: new HarmonyMethod(soundPrefix));
                _breaker.Success();
            }
            catch (Exception ex)
            {
                _shellActivate = null;
                _bulletSoundUpdate = null;
                _exceptions.Add(Name + " patch install", ex);
                _logger.LogWarning(Name + " unavailable; vanilla combat presentation remains unchanged: " + ex.Message);
            }
            SetEnabled(_configuration.CombatPresentationEnabled.Value);
        }

        public void OnRaidStarted()
        {
            _raidActive = true;
            _nextBulletSoundUpdate = 0f;
            _culledDistantShells = 0;
            _skippedBulletSoundFrames = 0;
        }

        public void OnRaidEnded()
        {
            _raidActive = false;
            _nextBulletSoundUpdate = 0f;
        }

        public void SetEnabled(bool enabled)
        {
            if (enabled && !IsAvailable) return;
            IsEnabled = enabled;
            _configuration.CombatPresentationEnabled.Value = enabled;
            if (!enabled) _nextBulletSoundUpdate = 0f;
        }

        public void Shutdown()
        {
            _raidActive = false;
            IsEnabled = false;
            if (_harmony != null)
            {
                if (_shellActivate != null) _harmony.Unpatch(_shellActivate, HarmonyPatchType.Prefix, _harmony.Id);
                if (_bulletSoundUpdate != null) _harmony.Unpatch(_bulletSoundUpdate, HarmonyPatchType.Prefix, _harmony.Id);
            }
            _shellActivate = null;
            _bulletSoundUpdate = null;
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private bool ShouldActivateShell(Shell shell, Vector3 beginPoint)
        {
            if (!IsEnabled || !_raidActive || !_configuration.CullDistantShellPhysics.Value || shell == null) return true;
            try
            {
                if (!CameraClass.Exist || CameraClass.Instance == null || CameraClass.Instance.Camera == null) return true;
                float distance = Clamp(_configuration.DistantShellPhysicsDistance.Value, 10f, 100f);
                if ((CameraClass.Instance.Camera.transform.position - beginPoint).sqrMagnitude <= distance * distance) return true;

                // Ejected casings are visual/audio cosmetics. Completing a distant casing before
                // BouncingObject.Init avoids its trajectory raycasts and all per-frame updates.
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
            if (!IsEnabled || !_raidActive || !_configuration.BudgetBulletFlybyAudio.Value) return true;
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

        private void FailOpen(string operation, Exception ex)
        {
            _exceptions.Add(Name + " " + operation, ex);
            if (!_breaker.Failure()) return;
            IsEnabled = false;
            _configuration.CombatPresentationEnabled.Value = false;
            _logger.LogError(Name + " failed open and was disabled; vanilla behavior continues: " + ex);
        }

        private static bool ShellActivatePrefix(Shell __instance, Vector3 beginPoint)
            => _instance == null || _instance.ShouldActivateShell(__instance, beginPoint);

        private static bool BulletSoundUpdatePrefix()
            => _instance == null || _instance.ShouldUpdateBulletSounds();

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
        private static float Clamp(float value, float minimum, float maximum) => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
