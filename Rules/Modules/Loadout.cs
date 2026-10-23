using System;
using System.Collections.Generic;
using System.Linq;
using BoosterImplants;
using CellMenu;
using Enemies;
using GameData;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The three booster slots in the lobby become a loadout:
///   1st slot (Muted)      — resource PACK: ammo / medi / tool / disinfection (one full pack = 5 charges);
///   2nd slot (Bold)       — extra ITEM for the consumable slot: grenade, C-foam grenade, mines, fog repeller…;
///   3rd slot (Aggressive) — PERK: a built-in bio-tracker pulse.
/// Click a slot to cycle the choice. Choices are stored in the config and sent to everyone;
/// at drop-in the host hands every player their pack and item (spawned in the level and picked up for them,
/// so they behave like normal pickups). Stock boosters can't be selected while this is on (nothing is consumed);
/// their effects live on as booster syringes (module Syringes).
/// </summary>
public class Loadout : ModuleBase
{
    public override string Id => "Loadout";
    public override string Description => "Booster slots in the lobby become a loadout: resource pack, extra item, perk. The host hands them out at drop-in. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    // ---------- choices ----------

    private enum PackKind : byte { None, Ammo, Medi, Tool, Disinfection }
    private enum PerkKind : byte { None, BioTracker }

    private static readonly uint[] PackItemIds = { 0, 101, 102, 127, 132 };

    private class Choice
    {
        public PackKind Pack;
        public uint Item;
        public PerkKind Perk;
        public override string ToString() => $"pack {Pack}, item {Item}, perk {Perk}";
    }

    private static BepInEx.Configuration.ConfigEntry<string> _pack, _perk, _itemChoices, _botPack;
    private static BepInEx.Configuration.ConfigEntry<uint> _item, _botItem;
    private static BepInEx.Configuration.ConfigEntry<int> _packCharges, _itemAmount;
    private static BepInEx.Configuration.ConfigEntry<bool> _blockBoosters, _perkWalls, _perkSound;
    private static BepInEx.Configuration.ConfigEntry<float> _giveDelay, _perkInterval, _perkRange;

    protected override void OnBindConfig()
    {
        _pack = Bind("Pack", "Ammo", "Your resource pack (also set by clicking the 1st booster slot in the lobby): None, Ammo, Medi, Tool, Disinfection.");
        _item = Bind("Item", 136u, "Your extra item, an item ID (also set by clicking the 2nd booster slot). 0 — none.");
        _perk = Bind("Perk", "BioTracker", "Your perk (also set by clicking the 3rd booster slot): None, BioTracker.");
        _itemChoices = Bind("ItemChoices", "136,115,139,144,117,114,116,140",
            "Items the 2nd slot cycles through (item IDs): 136 grenade, 115 C-foam grenade, 139 explosive trip mine, 144 C-foam trip mine, 117 fog repeller, 114 glow sticks, 116 lock melter, 140 I2-LP syringe.");
        _packCharges = Bind("PackCharges", 5, "Charges in the pack you get (a full pack is 5).");
        _itemAmount = Bind("ItemAmount", -1, "How many of the extra item you get (-1 — as many as the biggest stack found in levels).");
        _botPack = Bind("BotPack", "None", "Pack the host gives each bot: None, Ammo, Medi, Tool, Disinfection.");
        _botItem = Bind("BotItem", 0u, "Extra item the host gives each bot (item ID, 0 — none).");
        _giveDelay = Bind("GiveDelay", 3f, "Seconds after drop-in before the host hands out the loadout.");
        _blockBoosters = Bind("BlockStockBoosters", true, "Stock boosters can't be selected (already selected ones are unselected, not used up).");
        _perkInterval = Bind("PerkScanInterval", 8f, "Bio-tracker perk: seconds between pulses.");
        _perkRange = Bind("PerkScanRange", 15f, "Bio-tracker perk: pulse radius (m).");
        _perkWalls = Bind("PerkThroughWalls", true, "Bio-tracker perk sees through walls.");
        _perkSound = Bind("PerkSound", true, "Bio-tracker perk beeps when it finds enemies.");
    }

    protected override void OnEnable()
    {
        Net.On(Net.LoadoutChoice, OnChoiceReceived);
        FrameTicker.OnUpdate += Tick;
    }

    private static Choice Local() => new()
    {
        Pack = Enum.TryParse<PackKind>(_pack.Value, true, out var p) ? p : PackKind.None,
        Item = _item.Value,
        Perk = Enum.TryParse<PerkKind>(_perk.Value, true, out var k) ? k : PerkKind.None,
    };

    private static Choice Bot() => new()
    {
        Pack = Enum.TryParse<PackKind>(_botPack.Value, true, out var p) ? p : PackKind.None,
        Item = _botItem.Value,
    };

    private static List<uint> ItemChoices()
    {
        var list = new List<uint> { 0 };
        foreach (var s in _itemChoices.Value.Split(',', ';', ' '))
            if (uint.TryParse(s.Trim(), out var id) && id != 0 && GameDataBlockBase<ItemDataBlock>.HasBlock(id) && !list.Contains(id)) list.Add(id);
        return list;
    }

    // ---------- network: everyone tells everyone what they picked ----------

    private static readonly Dictionary<ulong, Choice> _remote = new();
    private static float _nextBroadcast;

    private static void Broadcast()
    {
        var c = Local();
        var b = new byte[6];
        b[0] = (byte)c.Pack;
        BitConverter.GetBytes(c.Item).CopyTo(b, 1);
        b[5] = (byte)c.Perk;
        Net.SendToAll(Net.LoadoutChoice, b);
        _nextBroadcast = Time.time + 5f;
    }

    private static void OnChoiceReceived(ulong sender, byte[] b)
    {
        if (b.Length < 6) return;
        var c = new Choice { Pack = (PackKind)b[0], Item = BitConverter.ToUInt32(b, 1), Perk = (PerkKind)b[5] };
        bool changed = !_remote.TryGetValue(sender, out var old) || old.ToString() != c.ToString();
        _remote[sender] = c;
        if (changed)
        {
            ModInfo.Log.LogInfo($"[Loadout] {NameOf(sender)} picked {c}");
            RepaintAll();
        }
    }

    private static string NameOf(ulong lookup)
    {
        try
        {
            foreach (var p in SNet.Slots.SlottedPlayers) if (p != null && p.Lookup == lookup) return p.NickName;
        }
        catch { }
        return lookup.ToString();
    }

    // ---------- lobby UI: the booster slots ----------

    private static void Cycle(BoosterImplantCategory category)
    {
        switch (category)
        {
            case BoosterImplantCategory.Muted:
            {
                var cur = Local().Pack;
                var next = (PackKind)(((int)cur + 1) % Enum.GetValues(typeof(PackKind)).Length);
                _pack.Value = next.ToString();
                break;
            }
            case BoosterImplantCategory.Bold:
            {
                var list = ItemChoices();
                int i = list.IndexOf(_item.Value);
                _item.Value = list[(i + 1) % list.Count];
                break;
            }
            case BoosterImplantCategory.Aggressive:
            {
                var cur = Local().Perk;
                var next = (PerkKind)(((int)cur + 1) % Enum.GetValues(typeof(PerkKind)).Length);
                _perk.Value = next.ToString();
                break;
            }
        }
        ModInfo.Log.LogInfo($"[Loadout] my choice: {Local()}");
        Broadcast();
        RepaintAll();
    }

    [HarmonyPatch(typeof(CM_BoosterImplantSlot), nameof(CM_BoosterImplantSlot.OnBtnPress))]
    private static class Patch_SlotPress
    {
        private static bool Prefix(CM_BoosterImplantSlot __instance)
        {
            try
            {
                var p = __instance.m_player;
                if (p != null && p.IsLocal) Cycle(__instance.m_category);
                Paint(__instance);
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] slot click: {e.Message}"); }
            return false; // never open the stock booster picker
        }
    }

    // In case the picker is opened some other way.
    [HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.ShowBoosterImplantSelect), new[] { typeof(BoosterImplantCategory), typeof(SNet_Player) })]
    private static class Patch_Select { private static bool Prefix() => false; }

    [HarmonyPatch(typeof(CM_PlayerLobbyBar), nameof(CM_PlayerLobbyBar.ShowBoosterImplantTeamView))]
    private static class Patch_TeamView { private static bool Prefix() => false; }

    [HarmonyPatch(typeof(CM_BoosterImplantSlot), nameof(CM_BoosterImplantSlot.UpdateSlot))]
    private static class Patch_UpdateSlot
    {
        private static void Postfix(CM_BoosterImplantSlot __instance) { try { Paint(__instance); } catch { } }
    }

    // Stock boosters can't be prepared for injection.
    [HarmonyPatch(typeof(PersistentInventoryManager), nameof(PersistentInventoryManager.PrepareBoosterImplantForInjection))]
    private static class Patch_Prepare
    {
        private static bool Prefix()
        {
            if (!_blockBoosters.Value) return true;
            ModInfo.Log.LogInfo("[Loadout] stock booster selection blocked");
            return false;
        }
    }

    private static readonly Dictionary<IntPtr, Sprite> _origEmptyIcon = new();
    private static int _paintLogs;

    private static void Paint(CM_BoosterImplantSlot slot)
    {
        if (slot == null) return;
        var p = slot.m_player;
        Choice c = p == null ? null : p.IsLocal ? Local() : _remote.TryGetValue(p.Lookup, out var r) ? r : null;

        string title, value; uint iconItem = 0;
        switch (slot.m_category)
        {
            case BoosterImplantCategory.Muted:
                title = Lang.T("PACK", "ПАЧКА");
                value = c == null ? "?" : PackName(c.Pack);
                if (c != null) iconItem = PackItemIds[(int)c.Pack];
                break;
            case BoosterImplantCategory.Bold:
                title = Lang.T("ITEM", "ПРЕДМЕТ");
                value = c == null ? "?" : ItemName(c.Item);
                if (c != null) iconItem = c.Item;
                break;
            default:
                title = Lang.T("PERK", "ПЕРК");
                value = c == null ? "?" : PerkName(c.Perk);
                break;
        }

        if (slot.m_categoryText != null) slot.m_categoryText.SetText(title);
        if (slot.m_statusText != null) { slot.m_statusText.gameObject.SetActive(true); slot.m_statusText.SetText(value); }
        if (slot.m_boosterIcon != null) slot.m_boosterIcon.gameObject.SetActive(false);
        if (slot.m_newItemIcon != null) slot.m_newItemIcon.SetActive(false);
        if (slot.m_injectedWarningIcon != null) slot.m_injectedWarningIcon.gameObject.SetActive(false);

        var empty = slot.m_emptyIcon;
        if (empty != null)
        {
            if (!_origEmptyIcon.ContainsKey(slot.Pointer)) _origEmptyIcon[slot.Pointer] = empty.sprite;
            var icon = iconItem != 0 ? Icon(iconItem) : null;
            empty.sprite = icon ?? _origEmptyIcon[slot.Pointer];
            empty.gameObject.SetActive(true);
        }
        if (_paintLogs++ < 6)
            ModInfo.Log.LogInfo($"[Loadout] slot {slot.m_category} of {(p == null ? "nobody" : p.NickName)}: {title} = {value}");
    }

    private static void RepaintAll()
    {
        try
        {
            var bars = CM_PlayerLobbyBar.instances;
            if (bars == null) return;
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                if (bar.m_boosterImplantsHeaderText != null) bar.m_boosterImplantsHeaderText.SetText(Lang.T("LOADOUT", "СНАРЯЖЕНИЕ"));
                var holder = bar.m_boosterImplantSlotHolder;
                if (holder == null || holder.m_categorySlots == null) continue;
                foreach (var cat in new[] { BoosterImplantCategory.Muted, BoosterImplantCategory.Bold, BoosterImplantCategory.Aggressive })
                    if (holder.m_categorySlots.TryGetValue(cat, out var slot)) Paint(slot);
            }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] repaint: {e.Message}"); }
    }

    private static string PackName(PackKind k) => k switch
    {
        PackKind.Ammo => Lang.T("Ammo", "Патроны"),
        PackKind.Medi => Lang.T("Medi", "Аптечка"),
        PackKind.Tool => Lang.T("Tool", "Инструменты"),
        PackKind.Disinfection => Lang.T("Disinfection", "Дезинфекция"),
        _ => "—",
    };

    private static string PerkName(PerkKind k) => k switch
    {
        PerkKind.BioTracker => Lang.T("Bio-tracker", "Биотрекер"),
        _ => "—",
    };

    private static string ItemName(uint id)
    {
        if (id == 0) return "—";
        var b = GameDataBlockBase<ItemDataBlock>.GetBlock(id);
        if (b == null) return $"#{id}";
        string n = null;
        try { n = (string)b.LocalizedName; } catch { }
        return string.IsNullOrWhiteSpace(n) ? b.publicName : n;
    }

    private static readonly Dictionary<uint, Sprite> _icons = new();

    /// <summary>Item icon from its HUDIcon resource path (the inventory icon). Null if it can't be loaded.</summary>
    private static Sprite Icon(uint itemId)
    {
        if (_icons.TryGetValue(itemId, out var s)) return s;
        Sprite sprite = null;
        string path = GameDataBlockBase<ItemDataBlock>.GetBlock(itemId)?.HUDIcon;
        try
        {
            if (!string.IsNullOrEmpty(path))
            {
                var obj = Resources.Load(path);
                sprite = obj?.TryCast<Sprite>();
                if (sprite == null)
                {
                    var tex = obj?.TryCast<Texture2D>();
                    if (tex != null) sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
                if (sprite == null)
                {
                    // Already loaded somewhere? Look it up by name.
                    string name = path.Substring(path.LastIndexOf('/') + 1);
                    foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Sprite>()))
                        if (o != null && o.name == name) { sprite = o.TryCast<Sprite>(); break; }
                }
            }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] icon of item {itemId}: {e.Message}"); }
        ModInfo.Log.LogInfo($"[Loadout] icon of item {itemId} ('{path}'): {(sprite != null ? "found" : "not found")}");
        _icons[itemId] = sprite;
        return sprite;
    }

    // ---------- tick ----------

    private static eGameStateName _lastState;
    private static float _giveAt = -1f, _nextLobbyCheck, _nextScan;
    private static bool _boostersCleared;

    private static void Tick()
    {
        var state = GameStateManager.CurrentStateName;
        if (state != _lastState)
        {
            if (state == eGameStateName.Lobby) { _boostersCleared = false; _nextBroadcast = 0f; }
            if (state == eGameStateName.InLevel && SNet.IsMaster) _giveAt = Time.time + Math.Max(0.5f, _giveDelay.Value);
            if (state != eGameStateName.InLevel) _giveAt = -1f;
            _lastState = state;
        }

        bool preGame = state == eGameStateName.Lobby || state == eGameStateName.Generating || state == eGameStateName.ReadyToStopElevatorRide
                       || state == eGameStateName.StopElevatorRide || state == eGameStateName.ReadyToStartLevel;
        if (preGame && Time.time >= _nextBroadcast) Broadcast();

        if (state == eGameStateName.Lobby && Time.time >= _nextLobbyCheck)
        {
            _nextLobbyCheck = Time.time + 1f;
            ClearStockBoosters();
            RepaintAll();
        }

        if (_giveAt > 0f && Time.time >= _giveAt)
        {
            _giveAt = -1f;
            try { GiveAll(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] give: {e}"); }
        }

        if (state == eGameStateName.InLevel && Time.time >= _nextScan)
        {
            _nextScan = Time.time + Math.Max(1f, _perkInterval.Value);
            if (Local().Perk == PerkKind.BioTracker) try { PerkScan(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] perk: {e.Message}"); }
        }
    }

    private static void ClearStockBoosters()
    {
        if (!_blockBoosters.Value || _boostersCleared) return;
        try
        {
            bool any = false;
            foreach (var cat in new[] { BoosterImplantCategory.Muted, BoosterImplantCategory.Bold, BoosterImplantCategory.Aggressive })
                if (PersistentInventoryManager.GetActiveBoosterImplant(cat) != null) any = true;
            if (any)
            {
                PersistentInventoryManager.UnPrepareAllBoosterImplants();
                ModInfo.Log.LogInfo("[Loadout] stock boosters unselected (not used up)");
            }
            _boostersCleared = true;
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] boosters: {e.Message}"); _boostersCleared = true; }
    }

    // ---------- host: hand out the loadout at drop-in ----------

    private static void GiveAll()
    {
        if (!SNet.IsMaster) return;
        int players = 0;
        foreach (var p in SNet.Slots.SlottedPlayers.ToArray())
        {
            if (p == null) continue;
            Choice c = p.IsLocal ? Local() : p.IsBot ? Bot() : _remote.TryGetValue(p.Lookup, out var r) ? r : null;
            if (c == null) { ModInfo.Log.LogWarning($"[Loadout] no loadout received from {p.NickName} (no mod or old version?) — nothing given"); continue; }
            players++;
            ModInfo.Log.LogInfo($"[Loadout] giving {p.NickName}: {c}");
            if (c.Pack != PackKind.None) Give(p, PackItemIds[(int)c.Pack], Math.Max(1, _packCharges.Value) * 20f);
            if (c.Item != 0) Give(p, c.Item, -1f);
        }
        ModInfo.Log.LogInfo($"[Loadout] handed out loadouts to {players} players");
    }

    /// <summary>Spawns the item in the level next to the player and picks it up for them (like a normal pickup).</summary>
    private static void Give(SNet_Player player, uint itemId, float ammo)
    {
        var agent = GameUtil.AgentOf(player);
        var block = GameDataBlockBase<ItemDataBlock>.GetBlock(itemId);
        if (agent == null || block == null) { ModInfo.Log.LogWarning($"[Loadout] can't give item {itemId} to {player.NickName}: {(agent == null ? "no agent" : "no such item")}"); return; }
        if (!ItemSpawnManager.ItemHasMode(itemId, ItemMode.Pickup)) { ModInfo.Log.LogWarning($"[Loadout] item {itemId} '{block.publicName}' has no pickup model loaded — skipped"); return; }
        if (PlayerBackpackManager.TryGetItem(player, block.inventorySlot, out var existing) && existing != null)
        {
            ModInfo.Log.LogInfo($"[Loadout] {player.NickName} already has something in {block.inventorySlot} — {block.publicName} not given");
            return;
        }
        if (ammo < 0f)
        {
            ammo = _itemAmount.Value > 0 ? _itemAmount.Value : Math.Max(1, block.ConsumableAmmoMax);
        }
        ModInfo.Log.LogInfo($"[Loadout] spawning {block.publicName} ({itemId}, slot {block.inventorySlot}, ammo {ammo}; level stacks are {block.ConsumableAmmoMin}–{block.ConsumableAmmoMax}) for {player.NickName}");

        var custom = new pItemData_Custom { ammo = ammo };
        var data = new pItemData { itemID_gearCRC = itemId, slot = block.inventorySlot, custom = custom };
        var target = player;
        var cb = DelegateSupport.ConvertDelegate<ItemReplicationManager.delItemCallback>(
            new Action<ISyncedItem, PlayerAgent>((item, _) => PickUpFor(item, target, custom, itemId)));
        ItemReplicationManager.SpawnItem(data, cb, ItemMode.Pickup, agent.Position + Vector3.up * 0.3f, Quaternion.identity, agent.CourseNode, agent);
    }

    private static void PickUpFor(ISyncedItem item, SNet_Player player, pItemData_Custom custom, uint itemId)
    {
        try
        {
            var inLevel = item?.TryCast<ItemInLevel>();
            if (inLevel == null) { ModInfo.Log.LogWarning($"[Loadout] spawned item {itemId} is not a level item — left on the floor"); return; }
            var sync = inLevel.GetSyncComponent();
            if (sync == null) { ModInfo.Log.LogWarning($"[Loadout] item {itemId} has no sync — left on the floor"); return; }
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player, custom);
            ModInfo.Log.LogInfo($"[Loadout] item {itemId} picked up for {player.NickName}");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] pick up for {player?.NickName}: {e.Message} — the item stays on the floor next to them"); }
    }

    // ---------- perk: bio-tracker pulse ----------

    private static int _perkLogs;

    private static void PerkScan()
    {
        var agent = GameUtil.LocalAgent;
        if (agent == null || !agent.Alive) return;
        var origin = agent.EyePosition;
        var seen = new HashSet<IntPtr>();
        int count = 0, tagged = 0;
        foreach (var col in Physics.OverlapSphere(origin, _perkRange.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            if (!_perkWalls.Value && Physics.Linecast(origin, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
            count++;
            if (enemy.IsTagged) continue;
            ToolSyncManager.WantToTagEnemy(enemy);
            tagged++;
        }
        if (count > 0 && _perkSound.Value) CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, origin);
        if (count > 0 && _perkLogs++ < 10) ModInfo.Log.LogInfo($"[Loadout] bio-tracker perk: {count} enemies within {_perkRange.Value:0} m, newly tagged {tagged}");
    }
}
