using Comfort.Common;
using EFT;
using UnityEngine;

namespace TarkovPerformanceSuite.Core;

/// <summary>Represents the small set of lifecycle states relevant to runtime optimizations.</summary>
internal enum RaidState
{
    MainMenu,
    Loading,
    Started,
}

/// <summary>Detects raid entry and exit so features can allocate and restore state at safe boundaries.</summary>
internal sealed class RaidLifecycle
{
    private float _nextCheck;
    private GameWorld _world;

    internal RaidState State { get; private set; } = RaidState.MainMenu;
    internal GameWorld World
    {
        get { return _world; }
    }

    internal event System.Action<GameWorld> RaidStarted;
    internal event System.Action<GameWorld> RaidEnded;
    internal event System.Action<RaidState> StateChanged;

    internal void Tick(float now)
    {
        if (now < _nextCheck)
        {
            return;
        }

        _nextCheck = now + 0.5f;

        GameWorld detected = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (!ReferenceEquals(_world, null) && !ReferenceEquals(detected, _world))
        {
            if (State == RaidState.Started)
            {
                RaidEnded?.Invoke(_world);
            }

            _world = null;
            ChangeState(RaidState.MainMenu);
        }

        if (detected == null)
        {
            return;
        }

        if (_world == null)
        {
            _world = detected;
            ChangeState(RaidState.Loading);
        }

        if (State != RaidState.Started && HasLocalPlayer(_world))
        {
            ChangeState(RaidState.Started);
            RaidStarted?.Invoke(_world);
        }
    }

    internal void Shutdown()
    {
        if (State == RaidState.Started && !ReferenceEquals(_world, null))
        {
            RaidEnded?.Invoke(_world);
        }

        _world = null;
        ChangeState(RaidState.MainMenu);
    }

    private static bool HasLocalPlayer(GameWorld world)
    {
        if (world == null || world.RegisteredPlayers == null)
        {
            return false;
        }

        for (int i = 0; i < world.RegisteredPlayers.Count; i++)
        {
            if (world.RegisteredPlayers[i] is Player player && player != null && player.IsYourPlayer)
            {
                return true;
            }
        }
        return false;
    }

    private void ChangeState(RaidState value)
    {
        if (State == value)
        {
            return;
        }

        State = value;
        StateChanged?.Invoke(value);
    }
}
