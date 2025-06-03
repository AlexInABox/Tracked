using System.Collections.Generic;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using UnityEngine;

namespace Sized;

public static class EventHandlers
{
    private static readonly Dictionary<int, Vector3> CachedSizes = new();

    public static void RegisterEvents()
    {
        PlayerEvents.Spawned += OnPlayerSpawned;
    }

    public static void UnregisterEvents()
    {
        PlayerEvents.Spawned -= OnPlayerSpawned;
    }

    private static void OnPlayerSpawned(PlayerSpawnedEventArgs ev)
    {
        if (Plugin.Instance.Config.OnlyHumans && !ev.Player.IsHuman) return;

        if (CachedSizes.ContainsKey(ev.Player.PlayerId))
        {
            ev.Player.SetScale(CachedSizes[ev.Player.PlayerId]);
            return;
        }

        CachedSizes[ev.Player.PlayerId] = Utilities.GetRandomScale();
        ev.Player.SetScale(CachedSizes[ev.Player.PlayerId]);
    }
}