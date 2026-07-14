using System;
using System.Collections.Generic;

namespace TarkovPerformanceSuite.Features
{
    public enum EntityKind
    {
        LocalPlayer,
        RemoteHuman,
        RemoteAI,
        UnknownPlayerLikeEntity,
        Corpse,
        NonPlayer
    }

    public readonly struct EntitySignals
    {
        public EntitySignals(bool isPlayer, bool isLocal, bool hasVerifiedBotOwner, bool isFikaObserved, bool? isFikaObservedAi, bool isCorpse)
        {
            IsPlayer = isPlayer;
            IsLocal = isLocal;
            HasVerifiedBotOwner = hasVerifiedBotOwner;
            IsFikaObserved = isFikaObserved;
            IsFikaObservedAi = isFikaObservedAi;
            IsCorpse = isCorpse;
        }

        public bool IsPlayer { get; }
        public bool IsLocal { get; }
        public bool HasVerifiedBotOwner { get; }
        public bool IsFikaObserved { get; }
        public bool? IsFikaObservedAi { get; }
        public bool IsCorpse { get; }
    }

    public static class EntityClassifierLogic
    {
        public static EntityKind Classify(EntitySignals signals)
        {
            if (signals.IsCorpse) return EntityKind.Corpse;
            if (!signals.IsPlayer) return EntityKind.NonPlayer;
            if (signals.IsLocal) return EntityKind.LocalPlayer;
            if (signals.HasVerifiedBotOwner) return EntityKind.RemoteAI;
            if (signals.IsFikaObserved && signals.IsFikaObservedAi == true) return EntityKind.RemoteAI;
            if (signals.IsFikaObserved && signals.IsFikaObservedAi == false) return EntityKind.RemoteHuman;
            return EntityKind.UnknownPlayerLikeEntity;
        }
    }

    public readonly struct ValidatedConfiguration
    {
        public ValidatedConfiguration(
            double captureSeconds,
            double shadowDistance,
            double updateIntervalSeconds,
            double shadowMinimumDistance,
            double shadowTargetFps,
            double skinningDistance,
            double skinningUpdateIntervalSeconds,
            double skinningOffscreenHoldSeconds)
        {
            CaptureSeconds = captureSeconds;
            ShadowDistance = shadowDistance;
            UpdateIntervalSeconds = updateIntervalSeconds;
            ShadowMinimumDistance = shadowMinimumDistance;
            ShadowTargetFps = shadowTargetFps;
            SkinningDistance = skinningDistance;
            SkinningUpdateIntervalSeconds = skinningUpdateIntervalSeconds;
            SkinningOffscreenHoldSeconds = skinningOffscreenHoldSeconds;
        }
        public double CaptureSeconds { get; }
        public double ShadowDistance { get; }
        public double UpdateIntervalSeconds { get; }
        public double ShadowMinimumDistance { get; }
        public double ShadowTargetFps { get; }
        public double SkinningDistance { get; }
        public double SkinningUpdateIntervalSeconds { get; }
        public double SkinningOffscreenHoldSeconds { get; }
    }

    public static class ConfigurationValidator
    {
        public static ValidatedConfiguration Validate(double captureSeconds, double shadowDistance, double updateIntervalSeconds)
        {
            return Validate(captureSeconds, shadowDistance, updateIntervalSeconds, 60, 60, 80, 0.1, 0.5);
        }

        public static ValidatedConfiguration Validate(
            double captureSeconds,
            double shadowDistance,
            double updateIntervalSeconds,
            double shadowMinimumDistance,
            double shadowTargetFps,
            double skinningDistance,
            double skinningUpdateIntervalSeconds,
            double skinningOffscreenHoldSeconds)
        {
            double validatedShadowDistance = Clamp(shadowDistance, 20, 1000);
            return new ValidatedConfiguration(
                Clamp(captureSeconds, 5, 900),
                validatedShadowDistance,
                Clamp(updateIntervalSeconds, 0.1, 5),
                Clamp(shadowMinimumDistance, 20, validatedShadowDistance),
                Clamp(shadowTargetFps, 20, 240),
                Clamp(skinningDistance, 20, 1000),
                Clamp(skinningUpdateIntervalSeconds, 0.05, 5),
                Clamp(skinningOffscreenHoldSeconds, 0.1, 10));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            return value < min ? min : value > max ? max : value;
        }
    }

    public sealed class AdaptiveDistanceController
    {
        private double _smoothedFrameMs;
        private double _pressureSeconds;
        private double _recoverySeconds;

        public double EffectiveDistance { get; private set; }
        public double SmoothedFrameMs => _smoothedFrameMs;

        public void Reset(double maximumDistance)
        {
            EffectiveDistance = maximumDistance;
            _smoothedFrameMs = 0;
            _pressureSeconds = 0;
            _recoverySeconds = 0;
        }

        public double Update(double deltaSeconds, double frameTimeMs, double maximumDistance, double minimumDistance, double targetFps)
        {
            maximumDistance = Math.Max(20, maximumDistance);
            minimumDistance = Math.Max(20, Math.Min(maximumDistance, minimumDistance));
            targetFps = Math.Max(20, Math.Min(240, targetFps));
            deltaSeconds = Math.Max(0, Math.Min(0.25, deltaSeconds));

            double targetFrameMs = 1000.0 / targetFps;
            if (EffectiveDistance <= 0 || EffectiveDistance > maximumDistance) EffectiveDistance = maximumDistance;
            if (_smoothedFrameMs <= 0) _smoothedFrameMs = targetFrameMs;
            double alpha = 1.0 - Math.Exp(-deltaSeconds / 0.5);
            _smoothedFrameMs += (frameTimeMs - _smoothedFrameMs) * alpha;

            if (_smoothedFrameMs > targetFrameMs + 1.0)
            {
                _pressureSeconds += deltaSeconds;
                _recoverySeconds = 0;
                if (_pressureSeconds >= 0.75)
                {
                    EffectiveDistance = Math.Max(minimumDistance, EffectiveDistance - 15.0);
                    _pressureSeconds = 0;
                }
            }
            else if (_smoothedFrameMs < targetFrameMs - 1.5)
            {
                _recoverySeconds += deltaSeconds;
                _pressureSeconds = 0;
                if (_recoverySeconds >= 4.0)
                {
                    EffectiveDistance = Math.Min(maximumDistance, EffectiveDistance + 10.0);
                    _recoverySeconds = 0;
                }
            }
            else
            {
                _pressureSeconds = 0;
                _recoverySeconds = 0;
            }

            if (EffectiveDistance < minimumDistance) EffectiveDistance = minimumDistance;
            return EffectiveDistance;
        }
    }

    public sealed class CircuitBreaker
    {
        private readonly int _threshold;
        public CircuitBreaker(int threshold)
        {
            if (threshold < 1) throw new ArgumentOutOfRangeException(nameof(threshold));
            _threshold = threshold;
        }
        public int ConsecutiveFailures { get; private set; }
        public bool IsOpen { get; private set; }
        public void Success() { ConsecutiveFailures = 0; }
        public bool Failure()
        {
            if (IsOpen) return true;
            ConsecutiveFailures++;
            if (ConsecutiveFailures >= _threshold) IsOpen = true;
            return IsOpen;
        }
        public void Reset() { ConsecutiveFailures = 0; IsOpen = false; }
    }

    public sealed class TimeScheduler
    {
        private readonly double _interval;
        private double _next;
        public TimeScheduler(double intervalSeconds)
        {
            if (intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            _interval = intervalSeconds;
        }
        public bool IsDue(double now)
        {
            if (now < _next) return false;
            _next = now + _interval;
            return true;
        }
        public void Reset(double now = 0) { _next = now; }
    }

    public sealed class OriginalStateCache<TKey, TState>
    {
        private readonly Dictionary<TKey, TState> _states = new Dictionary<TKey, TState>();
        public int Count => _states.Count;
        public bool Remember(TKey key, TState state)
        {
            if (_states.ContainsKey(key)) return false;
            _states.Add(key, state);
            return true;
        }
        public bool TryGet(TKey key, out TState state) => _states.TryGetValue(key, out state);
        public bool RestoreOne(TKey key, Action<TKey, TState> apply)
        {
            if (!_states.TryGetValue(key, out TState state)) return false;
            apply(key, state);
            _states.Remove(key);
            return true;
        }
        public void RestoreAll(Action<TKey, TState> apply)
        {
            foreach (KeyValuePair<TKey, TState> entry in _states) apply(entry.Key, entry.Value);
            _states.Clear();
        }
        public void Forget(TKey key) => _states.Remove(key);
        public void Clear() => _states.Clear();
    }
}
