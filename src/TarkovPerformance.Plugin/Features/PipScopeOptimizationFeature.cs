using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.CameraControl;
using HarmonyLib;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures;

/// <summary>Reports PiP camera decisions and render-target changes for diagnostics.</summary>
internal readonly struct PipScopeCounters
{
    internal PipScopeCounters(
        bool active,
        bool renderingDisabled,
        bool specialOpticBypass,
        int sourceResolution,
        int optimizedResolution,
        long renderedFrames,
        long reusedFrames,
        double averageRenderMs
    )
    {
        Active = active;
        RenderingDisabled = renderingDisabled;
        SpecialOpticBypass = specialOpticBypass;
        SourceResolution = sourceResolution;
        OptimizedResolution = optimizedResolution;
        RenderedFrames = renderedFrames;
        ReusedFrames = reusedFrames;
        AverageRenderMs = averageRenderMs;
    }

    internal bool Active { get; }
    internal bool RenderingDisabled { get; }
    internal bool SpecialOpticBypass { get; }
    internal int SourceResolution { get; }
    internal int OptimizedResolution { get; }
    internal long RenderedFrames { get; }
    internal long ReusedFrames { get; }
    internal double AverageRenderMs { get; }
}

/// <summary>
/// Keeps Tarkov's real PiP optic camera and vanilla update cadence, but renders it into a
/// smaller texture. HDR and camera scheduling remain entirely owned by the game because
/// changing either can break optic composition on some scopes.
/// </summary>
internal sealed class PipScopeOptimizationFeature : IPerformanceFeature
{
    private static PipScopeOptimizationFeature _instance;
    private readonly ManualLogSource _logger;
    private readonly PluginConfiguration _configuration;
    private readonly RecentExceptionLog _exceptions;
    private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
    private Harmony _harmony;
    private MethodInfo _lateUpdate;
    private bool _patchInstalled;
    private bool _raidActive;
    private bool _sessionActive;
    private bool _specialBypass;
    private GClass3687 _manager;
    private GClass3687 _resolutionManager;
    private Camera _camera;
    private Camera _disabledCamera;
    private bool _disabledCameraWasEnabled;
    private bool _renderingDisabled;
    private bool _originalMsaa;
    private bool _originalOcclusion;
    private int _sourceResolution;
    private int _optimizedResolution;
    private long _renderedFrames;
    private long _reusedFrames;
    private double _averageRenderMs;
    private ConfigEntry<bool> _replacementEnabledEntry;
    private float _nextReplacementLookup;
    private bool _replacementDesired;
    private bool _replacementActive;

    internal PipScopeOptimizationFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
    {
        _logger = logger;
        _configuration = configuration;
        _exceptions = exceptions;
    }

    public string Name
    {
        get { return "PiP Scope Camera Budget"; }
    }

    public bool IsAvailable
    {
        get { return _patchInstalled && !_breaker.IsOpen; }
    }

    public bool IsEnabled { get; private set; }
    internal PipScopeCounters Counters
    {
        get
        {
            return new PipScopeCounters(
                _sessionActive,
                _renderingDisabled && !_specialBypass,
                _specialBypass,
                _sourceResolution,
                _optimizedResolution,
                _renderedFrames,
                _reusedFrames,
                _averageRenderMs
            );
        }
    }

    internal string StatusText
    {
        get
        {
            if (_replacementActive)
            {
                return "active | main-camera zoom; secondary PiP camera released | Num3 restores full-resolution PiP";
            }

            if (_replacementDesired && _replacementEnabledEntry == null)
            {
                return "vanilla full-resolution PiP | replacement plugin missing";
            }

            if (!IsEnabled)
            {
                return _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";
            }

            if (!_patchInstalled)
            {
                return "unavailable (optic update hook missing)";
            }

            if (_specialBypass)
            {
                return "enabled | thermal/NVG unchanged";
            }

            if (!_sessionActive)
            {
                return "enabled | waiting for magnified optic";
            }

            return "active | " + _sourceResolution + " -> " + _optimizedResolution + " px | vanilla refresh/HDR";
        }
    }

    public void Initialize()
    {
        _breaker.Reset();
        _instance = this;
        try
        {
            _harmony = new Harmony("com.lucaswilluweit.tarkovperformancesuite.pip-scope-budget");
            _lateUpdate = AccessTools.Method(typeof(OpticComponentUpdater), "LateUpdate");
            MethodInfo postfix = AccessTools.Method(typeof(PipScopeOptimizationFeature), nameof(OpticLateUpdatePostfix));
            if (_lateUpdate == null || postfix == null)
            {
                throw new MissingMethodException("OpticComponentUpdater.LateUpdate was not found.");
            }

            _harmony.Patch(_lateUpdate, postfix: new HarmonyMethod(postfix));
            _patchInstalled = true;
        }
        catch (Exception ex)
        {
            _patchInstalled = false;
            _exceptions.Add(Name + " patch install", ex);
            _logger.LogWarning(Name + " unavailable; vanilla PiP is unchanged: " + ex.Message);
        }
        _renderingDisabled = false;
        SetEnabled(_configuration.PipScopeOptimizationEnabled.Value);
    }

    public void OnRaidStarted()
    {
        _raidActive = true;
        _renderedFrames = 0;
        _reusedFrames = 0;
        _averageRenderMs = 0;
        _renderingDisabled = false;
    }

    internal void Tick(bool masterEnabled)
    {
        _replacementDesired = masterEnabled && _configuration.PipReplacementEnabled.Value;
        if (_replacementEnabledEntry == null && Time.realtimeSinceStartup >= _nextReplacementLookup)
        {
            _nextReplacementLookup = Time.realtimeSinceStartup + 2f;
            ResolveReplacementPlugin();
        }

        if (_replacementEnabledEntry != null && _replacementEnabledEntry.Value != _replacementDesired)
        {
            try
            {
                _replacementEnabledEntry.Value = _replacementDesired;
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name + " PiP replacement sync", ex);
                _replacementEnabledEntry = null;
                _nextReplacementLookup = Time.realtimeSinceStartup + 5f;
            }
        }
        _replacementActive = _replacementEnabledEntry != null && _replacementEnabledEntry.Value;
        if (_replacementActive && _sessionActive)
        {
            RestoreAll();
        }
    }

    public void OnRaidEnded()
    {
        _raidActive = false;
        RestoreAll();
        _specialBypass = false;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && (!_patchInstalled || _breaker.IsOpen))
        {
            return;
        }

        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        _configuration.PipScopeOptimizationEnabled.Value = enabled;
        if (!enabled)
        {
            RestoreAll();
        }

        _logger.LogInfo(Name + " " + (enabled ? "enabled" : "disabled") + ". Vanilla PiP remains available.");
    }

    public void Shutdown()
    {
        _raidActive = false;
        IsEnabled = false;
        RestoreAll();
        if (_harmony != null && _lateUpdate != null)
        {
            _harmony.Unpatch(_lateUpdate, HarmonyPatchType.Postfix, _harmony.Id);
        }

        _patchInstalled = false;
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }

    private void ResolveReplacementPlugin()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.fiodor.pipdisabler", out BepInEx.PluginInfo info) || info?.Instance == null)
            {
                return;
            }

            Type settings = info.Instance.GetType().Assembly.GetType("PiPDisabler.Settings", false);
            FieldInfo field = settings?.GetField("ModEnabled", BindingFlags.Public | BindingFlags.Static);
            _replacementEnabledEntry = field?.GetValue(null) as ConfigEntry<bool>;
            if (_replacementEnabledEntry != null)
            {
                _logger.LogInfo(
                    "PiP-Disabler 1.5 integration active. Num3 switches between main-camera zoom and full-resolution vanilla PiP."
                );
            }
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name + " PiP replacement discovery", ex);
        }
    }

    private static void OpticLateUpdatePostfix()
    {
        _instance?.AfterOpticUpdate();
    }

    private void AfterOpticUpdate()
    {
        if (!IsEnabled || !_raidActive || !_patchInstalled || _replacementActive)
        {
            return;
        }

        try
        {
            if (!CameraClass.Exist || CameraClass.Instance == null)
            {
                ReleaseCameraOwnership(false);
                return;
            }

            GClass3687 manager = CameraClass.Instance.OpticCameraManager;
            OpticSight optic = manager?.CurrentOpticSight;
            Camera camera = manager?.Camera;
            if (manager == null || optic == null || camera == null || camera.targetTexture == null)
            {
                ReleaseCameraOwnership(false);
                return;
            }

            _specialBypass = _configuration.PipScopeSkipSpecialOptics.Value && IsSpecialOptic(optic);
            if (_specialBypass)
            {
                RestoreAll();
                _specialBypass = true;
                return;
            }

            if (!_sessionActive || camera != _camera || manager != _manager)
            {
                BeginSession(manager, camera);
            }

            RestoreDisabledCamera();

            EnsureOptimizedResolution();
            if (_configuration.PipScopeDisableMsaa.Value)
            {
                camera.allowMSAA = false;
            }

            camera.useOcclusionCulling = true;
            // Tarkov/Unity still renders the enabled optic camera exactly once at its normal
            // point in the frame. Never manually render or cap it here.
            _renderedFrames++;
            _breaker.Success();
        }
        catch (Exception ex)
        {
            _exceptions.Add(Name, ex);
            ReleaseCameraOwnership(true);
            if (_breaker.Failure())
            {
                IsEnabled = false;
                _configuration.PipScopeOptimizationEnabled.Value = false;
                RestoreAll();
                _logger.LogError(Name + " failed open and was disabled; vanilla PiP was restored: " + ex);
            }
        }
    }

    private void BeginSession(GClass3687 manager, Camera camera)
    {
        ReleaseCameraOwnership(false);
        if (_resolutionManager != null && _resolutionManager != manager && _sourceResolution > 0)
        {
            try
            {
                _resolutionManager.SetResolution(_sourceResolution);
            }
            catch { }
            _sourceResolution = 0;
            _optimizedResolution = 0;
        }
        _manager = manager;
        _resolutionManager = manager;
        _camera = camera;
        _originalMsaa = camera.allowMSAA;
        _originalOcclusion = camera.useOcclusionCulling;
        int current = camera.targetTexture != null ? camera.targetTexture.width : manager.OpticRenderResolution;
        if (_sourceResolution <= 0 || current != _optimizedResolution)
        {
            _sourceResolution = manager.OpticFinalResolution > 0 ? manager.OpticFinalResolution : current;
            if (_sourceResolution <= 0)
            {
                _sourceResolution = current;
            }
        }
        _optimizedResolution = CalculateResolution(_sourceResolution, _configuration.PipScopeResolutionScale.Value);
        _sessionActive = true;
    }

    private void EnsureOptimizedResolution()
    {
        if (_manager == null || _camera == null || _camera.targetTexture == null)
        {
            return;
        }

        int current = _camera.targetTexture.width;
        int expected = CalculateResolution(_sourceResolution, _configuration.PipScopeResolutionScale.Value);
        if (expected != _optimizedResolution)
        {
            _optimizedResolution = expected;
        }

        if (current == _optimizedResolution)
        {
            return;
        }

        // A resolution other than our own target means the game or user selected a new
        // vanilla optic resolution. Preserve it as the new restoration baseline.
        if (current != _optimizedResolution && current > 0 && current != _sourceResolution)
        {
            _sourceResolution = current;
            _optimizedResolution = CalculateResolution(_sourceResolution, _configuration.PipScopeResolutionScale.Value);
        }
        if (_optimizedResolution > 0 && _optimizedResolution < current)
        {
            _manager.SetResolution(_optimizedResolution);
        }
    }

    private void ReleaseCameraOwnership(bool keepSpecialBypass)
    {
        RestoreDisabledCamera();
        if (_sessionActive && _camera != null)
        {
            try
            {
                _camera.allowMSAA = _originalMsaa;
                _camera.useOcclusionCulling = _originalOcclusion;
            }
            catch { }
        }
        _sessionActive = false;
        _camera = null;
        _manager = null;
        if (!keepSpecialBypass)
        {
            _specialBypass = false;
        }
    }

    private void DisableCamera(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        if (_disabledCamera != camera)
        {
            RestoreDisabledCamera();
            // Do not claim a camera the game already disabled because it is not currently
            // rendering an aimed optic. The next active frame will be caught normally.
            if (!camera.enabled)
            {
                return;
            }

            _disabledCamera = camera;
            _disabledCameraWasEnabled = true;
        }
        camera.enabled = false;
    }

    private void RestoreDisabledCamera()
    {
        Camera camera = _disabledCamera;
        bool wasEnabled = _disabledCameraWasEnabled;
        _disabledCamera = null;
        _disabledCameraWasEnabled = false;
        if (camera != null)
        {
            try
            {
                camera.enabled = wasEnabled;
            }
            catch { }
        }
    }

    private void RestoreAll()
    {
        GClass3687 manager = _resolutionManager;
        int sourceResolution = _sourceResolution;
        ReleaseCameraOwnership(false);
        if (manager != null && sourceResolution > 0)
        {
            try
            {
                manager.SetResolution(sourceResolution);
            }
            catch { }
        }
        _sourceResolution = 0;
        _optimizedResolution = 0;
        _resolutionManager = null;
    }

    private static bool IsSpecialOptic(OpticSight optic)
    {
        ScopeData data = optic.ScopeData;
        if (data == null)
        {
            return false;
        }

        return (data.ThermalVisionData != null && data.ThermalVisionData.ThermalVision)
            || (data.NightVisionData != null && data.NightVisionData.NightVision);
    }

    private static int CalculateResolution(int source, float scale)
    {
        if (source <= 0)
        {
            return 0;
        }

        scale = Clamp(scale, 0.35f, 1f);
        int scaled = (int)Math.Round(source * scale / 64f) * 64;
        return Clamp(scaled, 256, source);
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min
            : value > max ? max
            : value;
    }

    private static float Clamp(float value, float min, float max)
    {
        return value < min ? min
            : value > max ? max
            : value;
    }
}
