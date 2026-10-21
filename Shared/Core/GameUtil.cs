using Il2CppInterop.Runtime.InteropTypes;
using Player;
using SNetwork;

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
}
