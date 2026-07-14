using System;
using BepInEx.Logging;
using TarkovPerformanceSuite.Configuration;
using TarkovPerformanceSuite.Core;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class FramePacingFeature : IPerformanceFeature
    {
        private readonly ManualLogSource _logger;
        private readonly PluginConfiguration _configuration;
        private readonly RecentExceptionLog _exceptions;
        private readonly CircuitBreaker _breaker = new CircuitBreaker(3);
        private FramePacingSnapshot _original;
        private bool _haveOriginal;
        private float _nextApply;

        internal FramePacingFeature(ManualLogSource logger, PluginConfiguration configuration, RecentExceptionLog exceptions)
        {
            _logger = logger;
            _configuration = configuration;
            _exceptions = exceptions;
        }

        public string Name => "CPU Frame Pacing";
        public bool IsAvailable => !_breaker.IsOpen;
        public bool IsEnabled { get; private set; }
        internal string StatusText => IsEnabled
            ? $"enabled | uploads {QualitySettings.asyncUploadTimeSlice} ms/{QualitySettings.asyncUploadBufferSize} MB | persistent {QualitySettings.asyncUploadPersistentBuffer} | loading {Application.backgroundLoadingPriority} | collision reuse {Physics.reuseCollisionCallbacks}"
            : _breaker.IsOpen ? "disabled (circuit breaker)" : "disabled";

        public void Initialize()
        {
            _breaker.Reset();
            SetEnabled(_configuration.FramePacingEnabled.Value);
        }

        public void OnRaidStarted()
        {
            _nextApply = 0;
            if (IsEnabled) Apply();
        }

        public void OnRaidEnded() { }

        public void SetEnabled(bool enabled)
        {
            if (enabled && _breaker.IsOpen) return;
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            _configuration.FramePacingEnabled.Value = enabled;
            if (enabled) Apply();
            else Restore();
        }

        public void Shutdown()
        {
            IsEnabled = false;
            Restore();
        }

        internal void Tick(float now)
        {
            if (!IsEnabled || now < _nextApply) return;
            _nextApply = now + 2f;
            Apply();
        }

        private void Apply()
        {
            try
            {
                if (!_haveOriginal)
                {
                    _original = FramePacingSnapshot.Capture();
                    _haveOriginal = true;
                }

                QualitySettings.asyncUploadTimeSlice = Clamp(_configuration.AsyncUploadTimeSlice.Value, 1, 8);
                QualitySettings.asyncUploadBufferSize = Clamp(_configuration.AsyncUploadBufferMb.Value, 16, 128);
                QualitySettings.asyncUploadPersistentBuffer = _configuration.AsyncUploadPersistentBuffer.Value;
                Application.backgroundLoadingPriority = _configuration.BackgroundLoadingLowPriority.Value ? ThreadPriority.Low : ThreadPriority.Normal;
                Physics.reuseCollisionCallbacks = _configuration.ReusePhysicsCollisionCallbacks.Value;
                _breaker.Success();
            }
            catch (Exception ex)
            {
                _exceptions.Add(Name, ex);
                _logger.LogError(Name + " failed open: " + ex);
                if (_breaker.Failure())
                {
                    IsEnabled = false;
                    _configuration.FramePacingEnabled.Value = false;
                    Restore();
                }
            }
        }

        private void Restore()
        {
            if (!_haveOriginal) return;
            try { _original.Apply(); }
            catch (Exception ex) { _exceptions.Add(Name + " restore", ex); }
            _haveOriginal = false;
        }

        private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;

        private readonly struct FramePacingSnapshot
        {
            private FramePacingSnapshot(int timeSlice, int bufferSize, bool persistent, ThreadPriority priority, bool reuseCollisions)
            {
                TimeSlice = timeSlice;
                BufferSize = bufferSize;
                Persistent = persistent;
                Priority = priority;
                ReuseCollisions = reuseCollisions;
            }

            private int TimeSlice { get; }
            private int BufferSize { get; }
            private bool Persistent { get; }
            private ThreadPriority Priority { get; }
            private bool ReuseCollisions { get; }

            internal static FramePacingSnapshot Capture() => new FramePacingSnapshot(
                QualitySettings.asyncUploadTimeSlice,
                QualitySettings.asyncUploadBufferSize,
                QualitySettings.asyncUploadPersistentBuffer,
                Application.backgroundLoadingPriority,
                Physics.reuseCollisionCallbacks);

            internal void Apply()
            {
                QualitySettings.asyncUploadTimeSlice = TimeSlice;
                QualitySettings.asyncUploadBufferSize = BufferSize;
                QualitySettings.asyncUploadPersistentBuffer = Persistent;
                Application.backgroundLoadingPriority = Priority;
                Physics.reuseCollisionCallbacks = ReuseCollisions;
            }
        }
    }
}
