using Il2CppInterop.Runtime.InteropTypes;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Core;

/// <summary>Small helpers on top of the game API.</summary>
public static class GameUtil
{
    public static bool InLevel => GameStateManager.CurrentStateName == eGameStateName.InLevel;

    /// <summary>Local player's PlayerAgent, or null (in menus, before spawn, etc.).</summary>
    public static PlayerAgent LocalAgent
    {
        get
        {
            if (!SNet.HasLocalPlayer) return null;
            return AgentOf(SNet.LocalPlayer);
        }
    }

    public static PlayerAgent AgentOf(SNet_Player player)
    {
        if (player == null || !player.HasPlayerAgent) return null;
        return ((Il2CppObjectBase)player.PlayerAgent).TryCast<PlayerAgent>();
    }

    public static bool IsLocal(SNet_Player player) => player != null && player.IsLocal;

    /// <summary>
    /// The agent is the local human player. IsLocallyOwned is also true for the host's bots — features driven by the
    /// keyboard (sprint, use, fire) must not act on them.
    /// </summary>
    public static bool IsLocalHuman(PlayerAgent agent) => agent != null && agent.IsLocallyOwned && agent.Owner != null && !agent.Owner.IsBot;

    /// <summary>All hits along a ray, nearest first (Physics.RaycastAll returns them in no particular order).</summary>
    public static RaycastHit[] RaycastAllSorted(Vector3 origin, Vector3 direction, float distance, int mask,
        QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
    {
        var raw = Physics.RaycastAll(origin, direction, distance, mask, triggers);
        var hits = new RaycastHit[raw.Length];
        for (int i = 0; i < raw.Length; i++) hits[i] = raw[i]; // copy out of the Il2Cpp array to sort it
        System.Array.Sort(hits, ByDistance);
        return hits;
    }

    private static readonly System.Comparison<RaycastHit> ByDistance = (a, b) => a.distance.CompareTo(b.distance);
}
