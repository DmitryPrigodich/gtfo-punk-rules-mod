using System;
using System.Collections.Generic;
using System.Linq;
using GameData;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Grenade. The game has an unreleased item 136 (CONSUMABLE_Grenade) with a ready-made GrenadeBase script
/// (radius, damage, delay, explosion). This module:
///  - names it and adds it to the level consumable distribution (boxes/lockers), like foam and mines;
///  - gives sentries a way to "throw" a grenade (synced, like a player throw) — for the grenade launcher sentry;
///  - sentry grenades explode on contact and are weaker (radius/damage from config).
/// Everyone needs it.
/// </summary>
public class Grenades : ModuleBase
{
    public override string Id => "Grenades";
    public override string Description => "Grenade: the game's unreleased item 136 — in consumable boxes and for the grenade launcher sentry. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    public const uint GrenadeItemId = 136;
    /// <summary>Clone of item 136 thrown only by the GL5 sentry — the item ID itself tells every client it's a sentry grenade.</summary>
    public const uint TurretGrenadeItemId = 4250;
    /// <summary>Item whose look the grenade borrows (C-Foam Grenade).</summary>
    private const uint LookItemId = 115;
    /// <summary>Mark in the item data: this grenade was thrown by a sentry (visible to all players).</summary>
    private const float TurretMark = -77f;

    private static BepInEx.Configuration.ConfigEntry<bool> _inLevels;
    private static BepInEx.Configuration.ConfigEntry<float> _weight, _turretRadius, _turretDamage, _turretFuse, _handRadius, _handDamage, _handDelay;
    private static BepInEx.Configuration.ConfigEntry<string> _nameEn, _nameRu;

    protected override void OnBindConfig()
    {
        _inLevels = Bind("InLevels", true, "Grenades show up in levels among consumables (boxes, lockers).");
        _weight = Bind("SpawnWeight", 0.6f, "How often a grenade shows up: fraction of the average consumable weight in the level distribution (1 — like a regular consumable).");
        _nameEn = Bind("NameEn", "Frag Grenade", "Grenade name (English).");
        _nameRu = Bind("NameRu", "Граната", "Grenade name (Russian).");
        _handRadius = Bind("HandRadius", 0f, "Hand grenade blast radius (m). 0 — as designed in the game.");
        _handDamage = Bind("HandDamage", 0f, "Hand grenade damage at the center. 0 — as designed in the game.");
        _handDelay = Bind("HandDelay", -1f, "Hand grenade fuse after impact (s). -1 — as designed in the game.");
        _turretRadius = Bind("TurretRadius", 3.5f, "Blast radius of the grenade launcher sentry's grenades (m; a mine's is bigger).");
        _turretDamage = Bind("TurretDamage", 40f, "Sentry grenade damage at the blast center.");
        _turretFuse = Bind("TurretFuse", 0f, "Sentry grenade fuse after impact (s). 0 — on contact.");
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _pending.Clear(); _calib.Clear(); _dumped = false; };
    }

    // ---------- data: name and distribution ----------

    private static void Inject()
    {
        var item = GameDataBlockBase<ItemDataBlock>.GetBlock(GrenadeItemId);
        if (item == null) { ModInfo.Log.LogWarning("[Grenades] item 136 is not in the game data — no grenade"); return; }
        int prefabs = (item.FirstPersonPrefabs?.Count ?? 0) + (item.InstancePrefabs?.Count ?? 0) + (item.PickupPrefabs?.Count ?? 0);
        ModInfo.Log.LogInfo($"[Grenades] item 136 '{item.publicName}', slot {item.inventorySlot}, models: first person {item.FirstPersonPrefabs?.Count ?? 0}, pickup {item.PickupPrefabs?.Count ?? 0}, thrown {item.InstancePrefabs?.Count ?? 0}, charges {item.ConsumableAmmoMin}–{item.ConsumableAmmoMax}");
        if (item.InstancePrefabs == null || item.InstancePrefabs.Count == 0) { ModInfo.Log.LogWarning("[Grenades] item 136 has no thrown model — leaving it alone"); return; }

        string name = Lang.T(_nameEn.Value, _nameRu.Value);
        item.publicName = name;
        try { item.LocalizedName = GearFactory.Text(name); } catch { }
        try { item.terminalItemShortName = "GRENADE"; item.terminalItemLongName = GearFactory.Text(name); } catch { }

        _named = name;

        // Sentry grenade: same item under its own fixed ID (prefabs load for it like for any item).
        if (!GameDataBlockBase<ItemDataBlock>.HasBlock(TurretGrenadeItemId))
        {
            var tg = GearFactory.CloneBlock(item);
            tg.persistentID = TurretGrenadeItemId;
            tg.name = "GtfoPunk_TurretGrenade";
            tg.publicName = Lang.T("Sentry Grenade", "Граната турели");
            try { tg.LocalizedName = GearFactory.Text(tg.publicName); } catch { }
            tg.registerInTerminalSystem = false;
            GameDataBlockBase<ItemDataBlock>.AddBlock(tg);
        }
        // Inventory icon of the C-Foam Grenade.
        var look = GameDataBlockBase<ItemDataBlock>.GetBlock(LookItemId);
        if (look != null && !string.IsNullOrEmpty(look.HUDIcon)) item.HUDIcon = look.HUDIcon;
    }

    private static string _named;
    private static bool _distributed;

    // Only touch the distribution once item models are loaded and the grenade has them —
    // otherwise a broken item in the distribution could break level generation.
    [HarmonyPatch(typeof(ItemSpawnManager), nameof(ItemSpawnManager.SetupItemPrefabs))]
    private static class Patch_Prefabs
    {
        private static void Postfix()
        {
            try { Distribute(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] distribution: {e.Message}"); }
            try { Reskin(GrenadeItemId); Reskin(TurretGrenadeItemId); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] look: {e.Message}"); }
        }
    }

    /// <summary>
    /// Item 136 ships with glow stick models. In every view (hand, third person, pickup, thrown) hide the glow stick
    /// meshes and put a copy of the C-Foam Grenade model in their place. Prefabs are changed once, so every spawned copy gets it.
    /// </summary>
    private static void Reskin(uint id)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return;
        int done = 0;
        for (int m = 0; m < perMode.Length; m++)
        {
            var dict = perMode[m];
            if (dict == null || !dict.ContainsKey(id) || !dict.ContainsKey(LookItemId)) continue;
            GameObject src = null;
            foreach (var cand in dict[LookItemId])
                if (cand != null && cand.GetComponentsInChildren<Renderer>(true).Length > 0) { src = cand; break; }
            if (src == null) continue;
            foreach (var dst in dict[id])
            {
                if (dst == null || dst.transform.Find("GtfoPunk_Look") != null) continue;
                var rs = dst.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                foreach (var r in rs) { r.enabled = false; r.forceRenderingOff = true; }
                if (TurretVariants.CopyModel(src, dst.transform, "GtfoPunk_Look") != null) done++;
            }
        }
        if (done > 0) ModInfo.Log.LogInfo($"[Grenades] item {id}: C-Foam Grenade look applied to {done} models");
    }

    private static void Distribute()
    {
        if (_distributed || _named == null || !_inLevels.Value) return;
        bool ok = ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.Pickup) && ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.Instance)
                  && ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.FirstPerson);
        if (!ok) { ModInfo.Log.LogWarning("[Grenades] not all grenade (136) models loaded — not adding it to level distributions"); return; }
        _distributed = true;
        string name = _named;
        int lists = 0;
        foreach (var d in GameDataBlockBase<ConsumableDistributionDataBlock>.GetAllBlocks())
        {
            if (d?.SpawnData == null || d.SpawnData.Count == 0) continue;
            bool has = false; float sum = 0f; int n = 0;
            foreach (var s in d.SpawnData) { if (s == null) continue; if (s.ItemID == GrenadeItemId) has = true; sum += s.Weight; n++; }
            if (has || n == 0) continue;
            var g = new ConsumableSpawnData { ItemID = GrenadeItemId, Weight = Math.Max(0.01f, sum / n * _weight.Value) };
            d.SpawnData.Add(g);
            lists++;
        }
        ModInfo.Log.LogInfo($"[Grenades] grenade '{name}' added to consumable distributions: {lists}");
    }

    // ---------- throw (for the sentry) ----------

    private static float _forceFactor = 1f;
    private static readonly List<(float time, Vector3 vel)> _calib = new();

    /// <summary>Throws a grenade synced for everyone (call on the host). Returns false if the item isn't available.</summary>
    public static bool Throw(PlayerAgent owner, Vector3 from, Vector3 velocity, AIG_CourseNode node)
    {
        uint id = ItemSpawnManager.ItemHasMode(TurretGrenadeItemId, ItemMode.Instance) ? TurretGrenadeItemId : GrenadeItemId;
        if (!ItemSpawnManager.ItemHasMode(id, ItemMode.Instance)) return false;
        var data = new pItemData { itemID_gearCRC = id };
        data.custom = new pItemData_Custom { ammo = TurretMark };
        var rot = velocity.sqrMagnitude > 0.01f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
        ItemReplicationManager.ThrowItem(data, null, ItemMode.Instance, from, rot, velocity * _forceFactor, from, node, owner);
        _calib.Add((Time.time, velocity));
        return true;
    }

    // ---------- behaviour of thrown grenades ----------

    private static readonly List<GrenadeBase> _pending = new();

    [HarmonyPatch(typeof(GrenadeBase), nameof(GrenadeBase.Start))]
    private static class Patch_Start
    {
        private static void Postfix(GrenadeBase __instance) { if (__instance != null) _pending.Add(__instance); }
    }

    private static bool _dumped;

    private static void Tick()
    {
        if (!_dumped && GameUtil.InLevel) { _dumped = true; try { Dump(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] {e.Message}"); } }
        if (_pending.Count == 0) return;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var g = _pending[i];
            _pending.RemoveAt(i);
            if (g == null) continue;
            try { Configure(g); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] {e.Message}"); }
        }
    }

    private static void Configure(GrenadeBase g)
    {
        bool turret = false;
        try { turret = g.ItemDataBlock != null && g.ItemDataBlock.persistentID == TurretGrenadeItemId; } catch { }
        if (!turret) try { turret = Math.Abs(g.Get_pItemData().custom.ammo - TurretMark) < 0.01f; } catch { }
        if (turret)
        {
            g.m_grenadeExplosionRadius = _turretRadius.Value;
            g.m_grenadeMaxDamage = (int)_turretDamage.Value;
            g.m_grenadeExplosionDelay = Math.Max(0f, _turretFuse.Value);
            Calibrate(g);
        }
        else
        {
            if (_handRadius.Value > 0f) g.m_grenadeExplosionRadius = _handRadius.Value;
            if (_handDamage.Value > 0f) g.m_grenadeMaxDamage = (int)_handDamage.Value;
            if (_handDelay.Value >= 0f) g.m_grenadeExplosionDelay = _handDelay.Value;
        }
        ModInfo.Log.LogInfo($"[Grenades] {(turret ? "sentry grenade" : "grenade")}: radius {g.m_grenadeExplosionRadius:0.#} m, damage {g.m_grenadeMaxDamage}, fuse {g.m_grenadeExplosionDelay:0.##} s");
    }

    /// <summary>Throw force tuning: compare the velocity we wanted with what we got (on the host).</summary>
    private static void Calibrate(GrenadeBase g)
    {
        if (!SNetwork.SNet.IsMaster || _calib.Count == 0) return;
        var rb = g.GetComponent<Rigidbody>() ?? g.GetComponentInChildren<Rigidbody>();
        var (time, want) = _calib[0];
        _calib.RemoveAt(0);
        if (rb == null || Time.time - time > 1f) return;
        float got = rb.velocity.magnitude, need = want.magnitude;
        if (got < 0.5f || need < 0.5f) { ModInfo.Log.LogInfo($"[Grenades] grenade speed {got:0.0} m/s (wanted {need:0.0}), mass {rb.mass:0.##}"); return; }
        float k = need / got;
        _forceFactor = Mathf.Clamp(_forceFactor * Mathf.Lerp(1f, k, 0.7f), 0.01f, 100f);
        ModInfo.Log.LogInfo($"[Grenades] grenade speed {got:0.0} m/s (wanted {need:0.0}), mass {rb.mass:0.##} → force factor {_forceFactor:0.###}");
    }

    private static void Dump()
    {
        ModInfo.Log.LogInfo($"[Grenades] sentry grenade item {TurretGrenadeItemId}: thrown model loaded = {ItemSpawnManager.ItemHasMode(TurretGrenadeItemId, ItemMode.Instance)}");
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return;
        for (int m = 0; m < perMode.Length; m++)
        {
            var dict = perMode[m];
            if (dict == null || !dict.ContainsKey(GrenadeItemId)) continue;
            var list = dict[GrenadeItemId];
            for (int k = 0; list != null && k < list.Count; k++)
            {
                var go = list[k];
                if (go == null) continue;
                var comps = go.GetComponentsInChildren<Component>(true).Select(c => c == null ? "null" : c.GetIl2CppType().Name).GroupBy(n => n).Select(x => $"{x.Key}x{x.Count()}");
                ModInfo.Log.LogInfo($"[Grenades] model 136 mode {(ItemMode)m} [{k}] '{go.name}': {string.Join(", ", comps)}");
            }
        }
    }
}
