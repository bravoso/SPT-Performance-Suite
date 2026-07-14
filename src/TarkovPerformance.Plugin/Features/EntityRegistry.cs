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
        internal bool IsAlive;
        internal bool IsVisible;
        internal float DistanceSquared;
        internal Animator[] Animators = Array.Empty<Animator>();
        internal Renderer[] Renderers = Array.Empty<Renderer>();
        internal SkinnedMeshRenderer[] SkinnedRenderers = Array.Empty<SkinnedMeshRenderer>();
        internal LODGroup[] LodGroups = Array.Empty<LODGroup>();
        internal readonly List<Animator> AnimatorBuffer = new List<Animator>(8);
        internal readonly List<Renderer> RendererBuffer = new List<Renderer>(32);
        internal readonly List<SkinnedMeshRenderer> SkinnedBuffer = new List<SkinnedMeshRenderer>(24);
        internal readonly List<LODGroup> LodBuffer = new List<LODGroup>(8);
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
        private float _nextSnapshotRefresh;
        private EntityCounts _cachedCounts;
        private GameWorld _world;
        private bool _haveLocalPosition;
        private Vector3 _localPosition;

        internal IEnumerable<TrackedEntity> Entities => _entities.Values;
        internal int Count => _entities.Count;

        internal void Start(GameWorld world)
        {
            Clear();
            _world = world;
            _nextCycle = 0;
        }

        internal void Tick(float now, float snapshotInterval)
        {
            if (_world == null || _world.RegisteredPlayers == null) return;
            if (_cursor != 0 || now >= _nextCycle)
            {
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
            if (now >= _nextSnapshotRefresh)
            {
                _nextSnapshotRefresh = now + Clamp(snapshotInterval, 0.05f, 0.25f);
                RefreshSnapshots();
            }
        }

        internal bool TryGetLocalPosition(out Vector3 position)
        {
            position = _localPosition;
            return _haveLocalPosition;
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
                    if (entity.IsAlive) livingAi++;
                    for (int i = 0; i < entity.Renderers.Length; i++)
                    {
                        Renderer renderer = entity.Renderers[i];
                        if (renderer == null) continue;
                        if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadows++;
                    }
                    if (entity.IsVisible) visibleAi++;
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
            _nextSnapshotRefresh = 0;
            _haveLocalPosition = false;
            _localPosition = default;
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
                entity.AnimatorBuffer.Clear();
                entity.RendererBuffer.Clear();
                entity.SkinnedBuffer.Clear();
                entity.LodBuffer.Clear();
                root.GetComponentsInChildren(true, entity.AnimatorBuffer);
                root.GetComponentsInChildren(true, entity.RendererBuffer);
                root.GetComponentsInChildren(true, entity.LodBuffer);
                for (int i = 0; i < entity.RendererBuffer.Count; i++)
                    if (entity.RendererBuffer[i] is SkinnedMeshRenderer skinned) entity.SkinnedBuffer.Add(skinned);
                entity.Animators = CopyToStableArray(entity.AnimatorBuffer, entity.Animators);
                entity.Renderers = CopyToStableArray(entity.RendererBuffer, entity.Renderers);
                entity.SkinnedRenderers = CopyToStableArray(entity.SkinnedBuffer, entity.SkinnedRenderers);
                entity.LodGroups = CopyToStableArray(entity.LodBuffer, entity.LodGroups);
                entity.NextComponentRefresh = now + 12f + ((id & int.MaxValue) % 7) * 0.37f;
            }
        }

        private void RefreshSnapshots()
        {
            _haveLocalPosition = false;
            foreach (TrackedEntity entity in _entities.Values)
            {
                if (entity.Kind == EntityKind.LocalPlayer && entity.Player != null)
                {
                    _localPosition = entity.Player.Transform.position;
                    _haveLocalPosition = true;
                    break;
                }
            }

            foreach (TrackedEntity entity in _entities.Values)
            {
                Player player = entity.Player;
                if (player == null) continue;
                entity.IsAlive = player.HealthController != null && player.HealthController.IsAlive;
                entity.DistanceSquared = _haveLocalPosition ? (player.Transform.position - _localPosition).sqrMagnitude : 0f;
                bool visible = player.IsYourPlayer || player.IsVisible;
                if (!visible)
                {
                    for (int i = 0; i < entity.Renderers.Length; i++)
                    {
                        Renderer renderer = entity.Renderers[i];
                        if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible)
                        {
                            visible = true;
                            break;
                        }
                    }
                }
                entity.IsVisible = visible;
            }
        }

        private static T[] CopyToStableArray<T>(List<T> source, T[] destination)
        {
            if (destination == null || destination.Length != source.Count) destination = new T[source.Count];
            source.CopyTo(destination, 0);
            return destination;
        }

        private void RemoveUnseen()
        {
            _removeBuffer.Clear();
            foreach (KeyValuePair<int, TrackedEntity> pair in _entities)
                if (pair.Value.Player == null || pair.Value.SeenGeneration != _generation) _removeBuffer.Add(pair.Key);
            for (int i = 0; i < _removeBuffer.Count; i++) _entities.Remove(_removeBuffer[i]);
        }

        private static float Clamp(float value, float minimum, float maximum)
            => float.IsNaN(value) || float.IsInfinity(value) ? minimum : value < minimum ? minimum : value > maximum ? maximum : value;
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
