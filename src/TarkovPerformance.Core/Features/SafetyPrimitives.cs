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
        public ValidatedConfiguration(double captureSeconds, double shadowDistance, double updateIntervalSeconds)
        {
            CaptureSeconds = captureSeconds;
            ShadowDistance = shadowDistance;
            UpdateIntervalSeconds = updateIntervalSeconds;
        }
        public double CaptureSeconds { get; }
        public double ShadowDistance { get; }
        public double UpdateIntervalSeconds { get; }
    }

    public static class ConfigurationValidator
    {
        public static ValidatedConfiguration Validate(double captureSeconds, double shadowDistance, double updateIntervalSeconds)
        {
            return new ValidatedConfiguration(
                Clamp(captureSeconds, 5, 900),
                Clamp(shadowDistance, 20, 1000),
                Clamp(updateIntervalSeconds, 0.1, 5));
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return min;
            return value < min ? min : value > max ? max : value;
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

