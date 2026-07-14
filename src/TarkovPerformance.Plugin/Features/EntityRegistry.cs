using System;
using System.Collections.Generic;
using EFT;
using TarkovPerformanceSuite.FikaAdapter;
using TarkovPerformanceSuite.Features;
using UnityEngine;

namespace TarkovPerformanceSuite.RuntimeFeatures
{
    internal sealed class TrackedEntity
    {
        internal Player Player;
        internal EntityKind Kind;
        internal int SeenGeneration;
        internal float NextComponentRefresh;
        internal Animator[] Animators = Array.Empty<Animator>();
        internal Renderer[] Renderers = Array.Empty<Renderer>();
        internal SkinnedMeshRenderer[] SkinnedRenderers = Array.Empty<SkinnedMeshRenderer>();
    }

    internal readonly struct EntityCounts
    {
        internal EntityCounts(int players, int local, int remoteHuman, int ai, int livingAi, int visibleAi, int corpses, int animators, int skinned, int shadow)
        {
            Players = players; LocalPlayers = local; RemoteHumans = remoteHuman; Ai = ai; LivingAi = livingAi;
            VisibleAi = visibleAi; Corpses = corpses; Animators = animators; SkinnedRenderers = skinned; ShadowRenderers = shadow;
        }
        internal int Players { get; }
        internal int LocalPlayers { get; }
        internal int RemoteHumans { get; }
        internal int Ai { get; }
        internal int LivingAi { get; }
        internal int VisibleAi { get; }
        internal int Corpses { get; }
        internal int Animators { get; }
        internal int SkinnedRenderers { get; }
        internal int ShadowRenderers { get; }
    }

    internal sealed class EntityRegistry
    {
        private readonly Dictionary<int, TrackedEntity> _entities = new Dictionary<int, TrackedEntity>(64);
        private readonly List<int> _removeBuffer = new List<int>(16);
        private int _generation;
        private int _cursor;
        private float _nextCycle;
        private float _nextCountRefresh;
        private EntityCounts _cachedCounts;
        private GameWorld _world;

        internal IEnumerable<TrackedEntity> Entities => _entities.Values;
        internal int Count => _entities.Count;

        internal void Start(GameWorld world)
        {
            Clear();
            _world = world;
            _nextCycle = 0;
        }

        internal void Tick(float now)
        {
            if (_world == null || _world.RegisteredPlayers == null) return;
            if (_cursor == 0 && now < _nextCycle) return;
            if (_cursor == 0) _generation++;

            int processed = 0;
            while (_cursor < _world.RegisteredPlayers.Count && processed < 4)
            {
                if (_world.RegisteredPlayers[_cursor] is Player player && player != null) RegisterOrRefresh(player, now);
                _cursor++;
                processed++;
            }

            if (_cursor >= _world.RegisteredPlayers.Count)
            {
                RemoveUnseen();
                _cursor = 0;
                _nextCycle = now + 1f;
            }
        }

        internal EntityCounts CountNow(float now)
        {
            if (now < _nextCountRefresh) return _cachedCounts;
            _nextCountRefresh = now + 0.25f;
            int players = 0, local = 0, humans = 0, ai = 0, livingAi = 0, visibleAi = 0, animators = 0, skinned = 0, shadows = 0;
            foreach (TrackedEntity entity in _entities.Values)
            {
                Player player = entity.Player;
                if (player == null) continue;
                players++;
                if (entity.Kind == EntityKind.LocalPlayer) local++;
                else if (entity.Kind == EntityKind.RemoteHuman) humans++;
                else if (entity.Kind == EntityKind.RemoteAI)
                {
                    ai++;
                    if (player.HealthController != null && player.HealthController.IsAlive) livingAi++;
                    bool visible = false;
                    for (int i = 0; i < entity.Renderers.Length; i++)
                    {
                        Renderer renderer = entity.Renderers[i];
                        if (renderer == null) continue;
                        if (renderer.isVisible) visible = true;
                        if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadows++;
                    }
                    if (visible) visibleAi++;
                }
                for (int i = 0; i < entity.Animators.Length; i++) if (entity.Animators[i] != null && entity.Animators[i].isActiveAndEnabled) animators++;
                for (int i = 0; i < entity.SkinnedRenderers.Length; i++) if (entity.SkinnedRenderers[i] != null && entity.SkinnedRenderers[i].enabled && entity.SkinnedRenderers[i].gameObject.activeInHierarchy) skinned++;
            }
            int corpses = _world != null && _world.ObservedPlayersCorpses != null ? _world.ObservedPlayersCorpses.Count : 0;
            _cachedCounts = new EntityCounts(players, local, humans, ai, livingAi, visibleAi, corpses, animators, skinned, shadows);
            return _cachedCounts;
        }

        internal void Clear()
        {
            _entities.Clear();
            _removeBuffer.Clear();
            _world = null;
            _cursor = 0;
            _generation = 0;
            _nextCountRefresh = 0;
            _cachedCounts = default;
        }

        private void RegisterOrRefresh(Player player, float now)
        {
            int id = player.GetInstanceID();
            if (!_entities.TryGetValue(id, out TrackedEntity entity))
            {
                entity = new TrackedEntity { Player = player, Kind = RuntimeEntityClassifier.Classify(player), NextComponentRefresh = 0 };
                _entities.Add(id, entity);
            }
            entity.SeenGeneration = _generation;
            if (now >= entity.NextComponentRefresh)
            {
                Transform root = player.PlayerBody != null ? player.PlayerBody.transform : player.gameObject.transform;
                entity.Animators = root.GetComponentsInChildren<Animator>(true);
                entity.Renderers = root.GetComponentsInChildren<Renderer>(true);
                entity.SkinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                entity.NextComponentRefresh = now + 10f;
            }
        }

        private void RemoveUnseen()
        {
            _removeBuffer.Clear();
            foreach (KeyValuePair<int, TrackedEntity> pair in _entities)
                if (pair.Value.Player == null || pair.Value.SeenGeneration != _generation) _removeBuffer.Add(pair.Key);
            for (int i = 0; i < _removeBuffer.Count; i++) _entities.Remove(_removeBuffer[i]);
        }
    }

    internal static class RuntimeEntityClassifier
    {
        internal static EntityKind Classify(Player player)
        {
            bool fikaObserved = FikaEntityAdapter.IsObservedPlayer(player);
            bool? fikaAi = fikaObserved ? FikaEntityAdapter.ReadObservedAi(player) : null;
            bool verifiedBot = player.AIData != null && player.AIData.BotOwner != null;
            return EntityClassifierLogic.Classify(new EntitySignals(true, player.IsYourPlayer, verifiedBot, fikaObserved, fikaAi, false));
        }
    }
}
