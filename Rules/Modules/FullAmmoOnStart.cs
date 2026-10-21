using System;
using GameData;
using GtfoPunk.Core;
using HarmonyLib;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Full ammo at the start: main, special and tool.
/// 1) In the player table (PlayerDataBlock) the starting reserve is set equal to the maximum — so the game itself
///    gives a full reserve to everyone who has the mod (bots — on the host).
/// 2) Host-side safety net: 2 s after drop-in every agent (bots included) gets ammo.
///    Ammo packs can't fill above the "pack cap", so point 1 is what matters.
/// </summary>
public class FullAmmoOnStart : ModuleBase
{
    public override string Id => "FullAmmoOnStart";
    public override string Description => "Everyone drops in with full ammo (main, special, tool).";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static BepInEx.Configuration.ConfigEntry<bool> _tool;
    private static float _giveAt = -1f;
    private static bool _given, _patchedData;

    protected override void OnBindConfig()
    {
        _tool = Bind("IncludeTool", true, "Also fill the tool (sentry, mines, foam).");
    }

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _given = false; _giveAt = -1f; };
        if (GameDataInit.IsLoaded) PatchData();
    }

    [HarmonyPatch(typeof(GameDataInit), nameof(GameDataInit.Initialize))]
    private static class Patch_DataInit
    {
        private static void Postfix() => PatchData();
    }

    private static void PatchData()
    {
        if (_patchedData) return;
        try
        {
            int n = 0;
            foreach (var b in GameDataBlockBase<PlayerDataBlock>.GetAllBlocks())
            {
                if (b == null) continue;
                b.AmmoStandardInitial = b.AmmoStandardInitialOnDropin = b.AmmoStandardMaxCap;
                b.AmmoSpecialInitial = b.AmmoSpecialInitialOnDropin = b.AmmoSpecialMaxCap;
                if (_tool.Value) b.AmmoClassInitial = b.AmmoClassInitialOnDropin = b.AmmoClassMaxCap;
                n++;
            }
            _patchedData = true;
            ModInfo.Log.LogInfo($"[FullAmmoOnStart] starting reserve = maximum (player blocks: {n})");
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

    private static void Tick()
    {
        if (_given || _giveAt < 0f || Time.time < _giveAt) return;
        _given = true;
        if (!SNet.IsMaster) return;

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
