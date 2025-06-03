using LabApi.Features.Enums;
using LabApi.Features.Wrappers;
using Mirror;
using UnityEngine;

namespace Sized;

public static class Utilities
{
    public static void SetScale(this Player player, Vector3 scale)
    {
        player.ReferenceHub.transform.localScale = scale;
        foreach (Player target in Player.GetAll(PlayerSearchFlags.AuthenticatedPlayers))
            NetworkServer.SendSpawnMessage(player.ReferenceHub.networkIdentity, target.Connection);
    }

    public static Vector3 GetRandomScale()
    {
        Vector3 maxScale = Plugin.Instance.Config.MaximumSize;
        Vector3 minScale = Plugin.Instance.Config.MinimumSize;

        float x = Random.Range(minScale.x, maxScale.x);
        float y = Random.Range(minScale.y, maxScale.y);
        float z = Random.Range(minScale.z, maxScale.z);
        return new Vector3(x, y, z);
    }
}