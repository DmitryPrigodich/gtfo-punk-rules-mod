using Agents;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Gadget "Soft boots" — always on, no battery: running, jumping and hard landings make only walking noise for the
/// enemies (sleepers don't hear you run past). The price: soft soles don't absorb a fall (more fall damage, from a
/// lower height) and you run a bit slower.
/// Enemies are run by the host, so the host applies it to whoever picked the boots (the choice is sent to everyone).
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<float> _bootsFallDamage, _bootsFallHeight, _bootsRunSpeed;

    private void BindBoots()
    {
        _bootsFallDamage = Bind("BootsFallDamage", 1.5f, "Soft boots: fall damage multiplier (1 — as usual).");
        _bootsFallHeight = Bind("BootsFallHeight", 0.7f, "Soft boots: the height a fall starts to hurt, as a share of the usual one.");
        _bootsRunSpeed = Bind("BootsRunSpeed", 0.92f, "Soft boots: running speed multiplier (1 — as usual).");
    }

    /// <summary>Who wears the boots (player slot → yes), refreshed once a second.</summary>
    private static readonly HashSet<int> _bootsSlots = new();
    private static float _nextBootsCheck;

    private static void BootsTick(bool inLevel, PerkKind localPerk)
    {
        if (!inLevel || Difficulty.Vanilla) { _bootsSlots.Clear(); RestoreLocomotion(); return; }
        if (Time.time >= _nextBootsCheck)
        {
            _nextBootsCheck = Time.time + 1f;
            _bootsSlots.Clear();
            try
            {
                foreach (var p in SNetwork.SNet.Slots.SlottedPlayers)
                {
                    if (p == null || p.IsBot) continue;
                    Choice c = p.IsLocal ? Local() : _remote.TryGetValue(p.Lookup, out var r) ? r : null;
                    if (c?.Perk == PerkKind.SoftBoots && GameUtil.AgentOf(p) is { } a) _bootsSlots.Add(a.PlayerSlotIndex);
                }
            }
            catch { }
        }
        if (localPerk == PerkKind.SoftBoots) ApplyLocomotion(); else RestoreLocomotion();
    }

    // ---------- the price: falls and running (own player — fall damage and speed are worked out locally) ----------

    private static PlayerAgent _bootsAgent;
    private static GameData.PlayerDataBlock _bootsOrig;

    /// <summary>Own player only: a copy of the player table with the boots' numbers, given to the local agent.</summary>
    private static void ApplyLocomotion()
    {
        var agent = GameUtil.LocalAgent;
        if (agent == null || agent == _bootsAgent) return;
        RestoreLocomotion();
        var orig = agent.PlayerData;
        if (orig == null) return;
        var data = GearFactory.CloneBlock(orig);
        float h = Mathf.Clamp(_bootsFallHeight.Value, 0.2f, 2f), d = Mathf.Clamp(_bootsFallDamage.Value, 0.1f, 5f);
        data.fallDamageMinHeight = orig.fallDamageMinHeight * h;
        data.fallDamageMaxHeight = orig.fallDamageMaxHeight * h;
        data.fallDamageMin = orig.fallDamageMin * d;
        data.fallDamageMax = orig.fallDamageMax * d;
        data.runMoveSpeed = orig.runMoveSpeed * Mathf.Clamp(_bootsRunSpeed.Value, 0.5f, 1.5f);
        agent.PlayerData = data;
        _bootsAgent = agent; _bootsOrig = orig;
        ModInfo.Log.LogInfo($"[Loadout] soft boots on: falls hurt from {data.fallDamageMinHeight:0.#} m (was {orig.fallDamageMinHeight:0.#}), damage ×{d:0.##}, run {data.runMoveSpeed:0.##} (was {orig.runMoveSpeed:0.##})");
    }

    private static void RestoreLocomotion()
    {
        if (_bootsAgent == null) return;
        try { if (_bootsOrig != null) _bootsAgent.PlayerData = _bootsOrig; } catch { }
        _bootsAgent = null; _bootsOrig = null;
    }

    // ---------- the point: quiet steps (host — the enemies hear the players' noise there) ----------

    private static int _bootsLogs;

    [HarmonyPatch(typeof(PlayerAgent), nameof(PlayerAgent.Noise), MethodType.Setter)]
    private static class Patch_Noise
    {
        private static void Prefix(PlayerAgent __instance, ref Agent.NoiseType value)
        {
            if (_bootsSlots.Count == 0 || value is not (Agent.NoiseType.Run or Agent.NoiseType.Jump or Agent.NoiseType.LoudLanding)) return;
            try
            {
                if (!_bootsSlots.Contains(__instance.PlayerSlotIndex)) return;
                if (_bootsLogs++ < 3) ModInfo.Log.LogInfo($"[Loadout] soft boots: {__instance.PlayerName} {value} → Walk");
                value = Agent.NoiseType.Walk;
            }
            catch { }
        }
    }
}
