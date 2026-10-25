using GameData;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Starting ammo by the party difficulty (module Difficulty): weapons and tool start with Share of the maximum reserve
/// (I 100% … VI 0%); DEFAULT — as in the game. Done through the player table (PlayerDataBlock), so the game itself
/// hands it out to everyone with the mod (bots — on the host). At 100% the host also tops everyone up 2 s after the drop.
/// </summary>
public class FullAmmoOnStart : ModuleBase
{
    public override string Id => "FullAmmoOnStart";
    public override string Description => "Starting ammo by the party difficulty (I — full … VI — empty; DEFAULT — as in the game).";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<bool> _tool;
    private static float _giveAt = -1f;
    private static bool _given;

    protected override void OnBindConfig()
    {
        _tool = Bind("IncludeTool", true, "The tool (sentry, mines, foam) follows the difficulty too.");
    }

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _given = false; _giveAt = -1f; };
        Difficulty.Changed += _ => Apply();
        if (GameDataInit.IsLoaded) Apply();
    }

    [HarmonyPatch(typeof(GameDataInit), nameof(GameDataInit.Initialize))]
    private static class Patch_DataInit
    {
        private static void Postfix() => Apply();
    }

    private class Original { public int Std, StdDrop, Spec, SpecDrop, Tool, ToolDrop; }
    private static readonly Dictionary<uint, Original> _orig = new();

    /// <summary>Starting reserve = the game's value (DEFAULT) or maximum × share.</summary>
    private static void Apply()
    {
        try
        {
            int n = 0;
            float k = Difficulty.Share;
            foreach (var b in GameDataBlockBase<PlayerDataBlock>.GetAllBlocks())
            {
                if (b == null) continue;
                if (!_orig.TryGetValue(b.persistentID, out var o))
                    _orig[b.persistentID] = o = new Original
                    {
                        Std = b.AmmoStandardInitial, StdDrop = b.AmmoStandardInitialOnDropin, Spec = b.AmmoSpecialInitial,
                        SpecDrop = b.AmmoSpecialInitialOnDropin, Tool = b.AmmoClassInitial, ToolDrop = b.AmmoClassInitialOnDropin,
                    };
                bool vanilla = Difficulty.Vanilla;
                b.AmmoStandardInitial = vanilla ? o.Std : Mathf.RoundToInt(b.AmmoStandardMaxCap * k);
                b.AmmoStandardInitialOnDropin = vanilla ? o.StdDrop : Mathf.RoundToInt(b.AmmoStandardMaxCap * k);
                b.AmmoSpecialInitial = vanilla ? o.Spec : Mathf.RoundToInt(b.AmmoSpecialMaxCap * k);
                b.AmmoSpecialInitialOnDropin = vanilla ? o.SpecDrop : Mathf.RoundToInt(b.AmmoSpecialMaxCap * k);
                bool tool = _tool.Value && !vanilla;
                b.AmmoClassInitial = tool ? Mathf.RoundToInt(b.AmmoClassMaxCap * k) : o.Tool;
                b.AmmoClassInitialOnDropin = tool ? Mathf.RoundToInt(b.AmmoClassMaxCap * k) : o.ToolDrop;
                n++;
            }
            ModInfo.Log.LogInfo(Difficulty.Vanilla ? $"[FullAmmoOnStart] starting reserve as in the game (player blocks: {n})"
                : $"[FullAmmoOnStart] starting reserve = {k * 100f:0}% of the maximum (player blocks: {n})");
        }
        catch (Exception e) { ModInfo.Log.LogError($"[FullAmmoOnStart] {e}"); }
    }

    [HarmonyPatch(typeof(GameStateManager), nameof(GameStateManager.ChangeState))]
    private static class Patch_State
    {
        private static void Postfix(eGameStateName nextState)
        {
            if (nextState == eGameStateName.Generating) { _given = false; _giveAt = -1f; }
            if (nextState == eGameStateName.InLevel && !_given) _giveAt = Time.time + 2f;
        }
    }

    /// <summary>Safety net at full ammo only (level I): the host tops everyone up 2 s after the drop.</summary>
    private static void Tick()
    {
        if (_given || _giveAt < 0f || Time.time < _giveAt) return;
        _given = true;
        if (!SNet.IsMaster || Difficulty.Vanilla || Difficulty.Share < 0.999f) return;

        float tool = _tool.Value ? 1f : 0f;
        var agents = PlayerManager.PlayerAgentsInLevel;
        if (agents == null) return;
        int n = 0;
        for (int i = 0; i < agents.Count; i++)
        {
            var p = agents[i]?.Owner;
            if (p == null) continue;
            for (int k = 0; k < 10; k++) PlayerBackpackManager.GiveAmmoToPlayer(p, 1f, 1f, tool);
            n++;
        }
        ModInfo.Log.LogInfo($"[FullAmmoOnStart] ammo given to agents: {n}");
    }
}
