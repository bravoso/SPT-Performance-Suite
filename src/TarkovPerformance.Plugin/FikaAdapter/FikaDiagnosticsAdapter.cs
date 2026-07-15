using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using TarkovPerformanceSuite.Features;
using TarkovPerformanceSuite.RuntimeFeatures;

namespace TarkovPerformanceSuite.FikaAdapter
{
    internal static class FikaEntityAdapter
    {
        private static readonly Dictionary<Type, FieldInfo> AiFields = new Dictionary<Type, FieldInfo>();

        internal static bool IsObservedPlayer(Player player) => player != null && player.GetType().FullName == "Fika.Core.Main.Players.ObservedPlayer";

        internal static bool? ReadObservedAi(Player player)
        {
            Type type = player.GetType();
            if (!AiFields.TryGetValue(type, out FieldInfo field))
            {
                field = type.GetField("IsObservedAI", BindingFlags.Instance | BindingFlags.Public);
                AiFields[type] = field;
            }
            if (field == null || field.FieldType != typeof(bool)) return null;
            try { return (bool)field.GetValue(player); }
            catch { return null; }
        }
    }

    internal sealed class FikaDiagnosticsAdapter
    {
        private Type _clientType;
        private object _client;
        private PropertyInfo _observedPlayers;
        private FieldInfo _serverFps;
        private PropertyInfo _singletonInstantiated;
        private PropertyInfo _singletonInstance;
        private float _nextDiscovery;

        internal bool Available => _clientType != null;
        internal int? ServerFps => ReadServerFps();

        internal void Tick(float now)
        {
            if (_client != null || now < _nextDiscovery) return;
            _nextDiscovery = now + 5f;
            if (_clientType == null)
            {
                _clientType = FindType("Fika.Core", "Fika.Core.Networking.FikaClient");
                if (_clientType == null) return;
                _observedPlayers = _clientType.GetProperty("ObservedPlayers", BindingFlags.Instance | BindingFlags.Public);
                _serverFps = _clientType.GetField("ServerFPS", BindingFlags.Instance | BindingFlags.Public);
                Type singleton = typeof(Singleton<>).MakeGenericType(_clientType);
                _singletonInstantiated = singleton.GetProperty("Instantiated", BindingFlags.Static | BindingFlags.Public);
                _singletonInstance = singleton.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            }
            try
            {
                if (_singletonInstantiated != null && _singletonInstance != null
                    && _singletonInstantiated.GetValue(null, null) is bool instantiated && instantiated)
                    _client = _singletonInstance.GetValue(null, null);
            }
            catch { _client = null; }
        }

        internal void GetCounts(out int observed, out int observedAi, out int visibleObservedAi)
        {
            observed = 0; observedAi = 0; visibleObservedAi = 0;
            if (_client == null || _observedPlayers == null) return;
            try
            {
                if (!(_observedPlayers.GetValue(_client, null) is IList list)) return;
                observed = list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is Player player) || player == null) continue;
                    if (RuntimeEntityClassifier.Classify(player) != EntityKind.RemoteAI) continue;
                    observedAi++;
                    if (player.IsVisible) visibleObservedAi++;
                }
            }
            catch { }
        }

        internal void Clear() { _client = null; _nextDiscovery = 0; }

        private int? ReadServerFps()
        {
            if (_client == null || _serverFps == null) return null;
            try { return (int)_serverFps.GetValue(_client); }
            catch { return null; }
        }

        private static Type FindType(string assemblyName, string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
                if (string.Equals(assemblies[i].GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return assemblies[i].GetType(fullName, false);
            return null;
        }
    }
}
