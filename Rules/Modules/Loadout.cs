using BoosterImplants;
using CellMenu;
using Enemies;
using GameData;
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
///   3rd slot (Aggressive) — GADGET: thermal goggles or a bio-tracker pulse, used with the gadget key (N).
/// Click a slot to cycle the choice. Choices are stored in the config and sent to everyone;
/// at drop-in the host hands every player their pack and item (spawned in the level and picked up for them,
/// so they behave like normal pickups). Stock boosters can't be selected while this is on (nothing is consumed);
/// their effects live on as booster syringes (module Syringes).
/// </summary>
public partial class Loadout : ModuleBase
{
    public override string Id => "Loadout";
    public override string Description => "Booster slots in the lobby become a loadout: resource pack, extra item, gadget (thermal / bio-tracker). The host hands them out at drop-in. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    // ---------- choices ----------

    private enum PackKind : byte { None, Ammo, Medi, Tool, Disinfection }
    private enum PerkKind : byte { None, BioTracker, Thermal, Hacker } // values travel over the network — never renumber

    private static readonly uint[] PackItemIds = { 0, 101, 102, 127, 132 };

    private class Choice
    {
        public PackKind Pack;
        public uint Item;
        public PerkKind Perk;
        public override string ToString() => $"pack {Pack}, item {Item}, perk {Perk}";
    }

    private static ConfigEntry<string> _pack, _perk, _itemChoices, _botPack;
    private static ConfigEntry<uint> _item, _botItem;
    private static ConfigEntry<int> _packCharges, _itemAmount;
    private static ConfigEntry<bool> _blockBoosters, _givePickup;
    private static ConfigEntry<string> _gadgetKey;
    private static ConfigEntry<float> _giveDelay;

    protected override void OnBindConfig()
    {
        _pack = Bind("Pack", "Ammo", "Your resource pack (also set by clicking the 1st booster slot in the lobby): None, Ammo, Medi, Tool, Disinfection.");
        _item = Bind("Item", 136u, "Your extra item, an item ID (also set by clicking the 2nd booster slot). 0 — none.");
        _perk = Bind("Perk", "BioTracker", "Your gadget (also set by clicking the 3rd booster slot): BioTracker, Thermal.");
        _gadgetKey = Bind("GadgetKey", "N", "Key that switches the gadget on/off: thermal goggles or the bio-tracker screen (Unity KeyCode name).");
        _itemChoices = Bind("ItemChoices", "136,115,139,144,117,114,116,140",
            "Items the 2nd slot cycles through (item IDs): 136 grenade, 115 C-foam grenade, 139 explosive trip mine, 144 C-foam trip mine, 117 fog repeller, 114 glow sticks, 116 lock melter, 140 I2-LP syringe.");
        _packCharges = Bind("PackCharges", 5, "Charges in the pack you get (a full pack is 5).");
        _itemAmount = Bind("ItemAmount", -1, "How many of the extra item you get (-1 — as many as the biggest stack found in levels).");
        _botPack = Bind("BotPack", "None", "Pack the host gives each bot: None, Ammo, Medi, Tool, Disinfection.");
        _botItem = Bind("BotItem", 0u, "Extra item the host gives each bot (item ID, 0 — none).");
        _giveDelay = Bind("GiveDelay", 3f, "Seconds after drop-in before the host hands out the loadout.");
        _givePickup = Bind("GiveAsPickup", false, "Old way of handing out (spawn a pickup next to the player and pick it up). false — straight into the backpack (the old way broke checkpoint captures).");
        _blockBoosters = Bind("BlockStockBoosters", true, "Stock boosters can't be selected (already selected ones are unselected, not used up).");
        BindBattery();
        BindRadar();
        BindThermal();
        BindIcons();
        BindGadgetHud();
        ConfigMigration.DropOrphans(Config, Id); // PerkScanInterval, BioTrackerAuto … (the pulse is gone)
    }

    protected override void OnEnable()
    {
        Net.On(Net.LoadoutChoice, OnChoiceReceived);
        Net.On(Net.AmmoFix, OnAmmoFixReceived);
        FrameTicker.OnUpdate += Tick;
    }

    /// <summary>Your choice. Nothing is ever left empty: unknown / "None" values fall back to the first option.</summary>
    private static Choice Local()
    {
        var pack = Enum.TryParse<PackKind>(_pack.Value, true, out var p) && p != PackKind.None ? p : PackKind.Ammo;
        var items = ItemChoices();
        uint item = items.Contains(_item.Value) ? _item.Value : items.Count > 0 ? items[0] : 0;
        var perk = Enum.TryParse<PerkKind>(_perk.Value, true, out var k) && k != PerkKind.None ? k : PerkKind.BioTracker;
        return new Choice { Pack = pack, Item = item, Perk = perk };
    }

    private static Choice Bot() => new()
    {
        Pack = Enum.TryParse<PackKind>(_botPack.Value, true, out var p) ? p : PackKind.None,
        Item = _botItem.Value,
    };

    private static List<uint> ItemChoices()
    {
        var list = new List<uint>();
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

    private static int Wrap(int i, int dir, int n) => ((i + dir) % n + n) % n;

    private static void Cycle(BoosterImplantCategory category, int dir = 1)
    {
        switch (category)
        {
            case BoosterImplantCategory.Muted:
            {
                int n = Enum.GetValues(typeof(PackKind)).Length; // 0 = None is skipped
                var next = (PackKind)(Wrap(Math.Max(0, (int)Local().Pack - 1), dir, n - 1) + 1);
                _pack.Value = next.ToString();
                break;
            }
            case BoosterImplantCategory.Bold:
            {
                var list = ItemChoices();
                if (list.Count == 0) break;
                int i = list.IndexOf(Local().Item);
                _item.Value = list[Wrap(Math.Max(i, dir > 0 ? -1 : 0), dir, list.Count)];
                break;
            }
            case BoosterImplantCategory.Aggressive:
            {
                int n = Enum.GetValues(typeof(PerkKind)).Length; // 0 = None is skipped
                var next = (PerkKind)(Wrap(Math.Max(0, (int)Local().Perk - 1), dir, n - 1) + 1);
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
    private static readonly Dictionary<IntPtr, (Quaternion, Vector3)> _origEmptyTf = new();

    /// <summary>Right click on one of your loadout slots — one step back.</summary>
    private static void SlotRightClick()
    {
        if (!Input.GetMouseButtonDown(1) || GameStateManager.CurrentStateName != eGameStateName.Lobby) return;
        try
        {
            var bars = CM_PlayerLobbyBar.instances;
            if (bars == null) return;
            foreach (var bar in bars)
            {
                var holder = bar?.m_boosterImplantSlotHolder;
                if (holder == null) continue;
                foreach (var slot in holder.GetComponentsInChildren<CM_BoosterImplantSlot>(false))
                {
                    if (slot == null || !slot.m_isHovering || slot.m_player == null || !slot.m_player.IsLocal) continue;
                    Cycle(slot.m_category, -1);
                    return;
                }
            }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] right click: {e.Message}"); }
    }
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
                title = Lang.T("GADGET", "ГАДЖЕТ");
                value = c == null ? "?" : PerkName(c.Perk);
                if (c != null) iconItem = GadgetIconId(c.Perk);
                break;
        }
        if (Difficulty.Vanilla) value = Lang.T("off (default)", "выкл (по умолч.)");

        if (slot.m_categoryText != null) slot.m_categoryText.SetText(title);
        if (slot.m_statusText != null) { slot.m_statusText.gameObject.SetActive(true); slot.m_statusText.SetText(value); }
        if (slot.m_boosterIcon != null) slot.m_boosterIcon.gameObject.SetActive(false);
        if (slot.m_newItemIcon != null) slot.m_newItemIcon.SetActive(false);
        if (slot.m_injectedWarningIcon != null) slot.m_injectedWarningIcon.gameObject.SetActive(false);

        var empty = slot.m_emptyIcon;
        if (empty != null)
        {
            if (!_origEmptyIcon.ContainsKey(slot.Pointer)) _origEmptyIcon[slot.Pointer] = empty.sprite;
            var reference = _origEmptyIcon[slot.Pointer];
            var icon = iconItem == 0 ? null
                : IconFile(iconItem, reference) ?? (_photos.Value ? Photo(iconItem, reference) ?? (iconItem < BioIconId ? Icon(iconItem) : null) : null);
            empty.sprite = icon ?? _origEmptyIcon[slot.Pointer];
            // The stock empty-slot mark is a tilted diamond: our pictures go upright and bigger.
            var et = empty.transform;
            if (!_origEmptyTf.ContainsKey(slot.Pointer)) _origEmptyTf[slot.Pointer] = (et.localRotation, et.localScale);
            var (rot0, scale0) = _origEmptyTf[slot.Pointer];
            if (icon != null) et.rotation = slot.m_categoryText != null ? slot.m_categoryText.transform.rotation : slot.transform.rotation; // upright like the text
            else et.localRotation = rot0;
            et.localScale = scale0;
            empty.gameObject.SetActive(icon != null || c == null); // text only when there's no picture
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
                if (holder == null) continue;
                // (Il2Cpp Dictionary<enum, …>.TryGetValue throws "__Canon" here — walk the slot components instead.)
                foreach (var slot in holder.GetComponentsInChildren<CM_BoosterImplantSlot>(true)) Paint(slot);
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
        PerkKind.Thermal => Lang.T("Thermal goggles", "Тепловизор"),
        PerkKind.Hacker => Lang.T("Hacking tool", "Инструмент взлома"),
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
    private static float _giveAt = -1f, _nextLobbyCheck, _lobbySince;
    private static bool _boostersCleared;

    private static void Tick()
    {
        SlotRightClick();
        var state = GameStateManager.CurrentStateName;
        if (state != _lastState)
        {
            if (state == eGameStateName.Lobby) { _boostersCleared = false; _nextBroadcast = 0f; _lobbySince = Time.time; }
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
            try { PhotoTick(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] photos: {e.Message}"); }
            if (Time.time - _lobbySince > 4f) ThermalLobbyPrep();
            RepaintAll();
        }

        if (_ammoJobs.Count > 0) AmmoFixTick();

        if (_giveAt > 0f && Time.time >= _giveAt)
        {
            _giveAt = -1f;
            try { GiveAll(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] give: {e}"); }
        }

        // DEFAULT difficulty: no gadgets at all (the hacking tool is everyone's again).
        var perk = Difficulty.Vanilla ? PerkKind.None : Local().Perk;
        bool inLevel = state == eGameStateName.InLevel;
        // The thermal material search is a scene-wide scan: do it while the elevator drops, not at first use.
        if (state == eGameStateName.StopElevatorRide && perk == PerkKind.Thermal) try { ThermalMaterial(); } catch { }
        try { ThermalTick(inLevel, perk == PerkKind.Thermal); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] thermal: {e.Message}"); }
        try { GadgetHudTick(inLevel, perk); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] gadget line: {e.Message}"); }
        try { RadarTick(inLevel, perk == PerkKind.BioTracker); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] bio-tracker: {e.Message}"); }
        try { HackerTick(inLevel, perk == PerkKind.Hacker); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] hacking tool: {e.Message}"); }
        BatteryTick(inLevel, perk);
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
        if (Difficulty.Vanilla) { ModInfo.Log.LogInfo("[Loadout] DEFAULT difficulty — no loadout handed out"); return; }
        int players = 0;
        foreach (var p in SNet.Slots.SlottedPlayers.ToArray())
        {
            if (p == null) continue;
            Choice c = p.IsLocal ? Local() : p.IsBot ? Bot() : _remote.TryGetValue(p.Lookup, out var r) ? r : null;
            if (c == null) { ModInfo.Log.LogWarning($"[Loadout] no loadout received from {p.NickName} (no mod or old version?) — nothing given"); continue; }
            players++;
            ModInfo.Log.LogInfo($"[Loadout] giving {p.NickName}: {c}");
            // Pack fill and item count follow the difficulty (I 100% … VI 0%); nothing below one charge / one item.
            int charges = Difficulty.Scale(Math.Max(1, _packCharges.Value));
            if (c.Pack != PackKind.None && charges > 0) Give(p, PackItemIds[(int)c.Pack], charges * 20f);
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
            ammo = Difficulty.Scale(_itemAmount.Value > 0 ? _itemAmount.Value : Math.Max(1, block.ConsumableAmmoMax));
            if (ammo < 1f) { ModInfo.Log.LogInfo($"[Loadout] {block.publicName}: none at this difficulty"); return; }
        }
        ModInfo.Log.LogInfo($"[Loadout] spawning {block.publicName} ({itemId}, slot {block.inventorySlot}, ammo {ammo}; level stacks are {block.ConsumableAmmoMin}–{block.ConsumableAmmoMax}) for {player.NickName}");

        var custom = new pItemData_Custom { ammo = ammo };
        var data = new pItemData { itemID_gearCRC = itemId, slot = block.inventorySlot, custom = custom };
        var target = player;
        if (!_givePickup.Value)
        {
            // Straight into the backpack, the way the game hands items out at drop-in. Spawning a level pickup and
            // picking it up left a broken item replicator behind: every checkpoint / migration capture then threw a
            // NullReferenceException (thousands of them — a huge log and hitches).
            try
            {
                PlayerBackpackManager.MasterAddItem(data, player);
                ModInfo.Log.LogInfo($"[Loadout] item {itemId} put into {player.NickName}'s backpack");
                FixAmmo(player, block.inventorySlot, ammo, itemId);
                return;
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] backpack add failed ({e.Message}) — spawning a pickup instead"); }
        }
        var cb = DelegateSupport.ConvertDelegate<ItemReplicationManager.delItemCallback>(
            new Action<ISyncedItem, PlayerAgent>((item, _) => PickUpFor(item, target, custom, itemId)));
        ItemReplicationManager.SpawnItem(data, cb, ItemMode.Pickup, agent.Position + Vector3.up * 0.3f, Quaternion.identity, agent.CourseNode, agent);
    }

    /// <summary>The pack / consumable ammo lives in the owner's ammo storage — make sure it's filled (owner-side).</summary>
    private static void FixAmmo(SNet_Player player, InventorySlot slot, float ammo, uint itemId)
    {
        if (player.IsLocal || player.IsBot) { QueueAmmoFix(player, slot, ammo, itemId); return; }
        var b = new byte[9];
        b[0] = (byte)slot;
        BitConverter.GetBytes(ammo).CopyTo(b, 1);
        BitConverter.GetBytes(itemId).CopyTo(b, 5);
        Net.SendTo(player, Net.AmmoFix, b);
    }

    private static void PickUpFor(ISyncedItem item, SNet_Player player, pItemData_Custom custom, uint itemId)
    {
        try
        {
            var inLevel = item?.TryCast<ItemInLevel>();
            if (inLevel == null) { ModInfo.Log.LogWarning($"[Loadout] spawned item {itemId} is not a level item — left on the floor"); return; }
            var sync = inLevel.GetSyncComponent();
            if (sync == null) { ModInfo.Log.LogWarning($"[Loadout] item {itemId} has no sync — left on the floor"); return; }
            // The spawn doesn't carry our ammo: put it on the level item first, then pick it up with it.
            try { inLevel.SetCustomData(custom, true); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] set item data: {e.Message}"); }
            sync.AttemptPickupInteraction(ePickupItemInteractionType.Pickup, player, custom);
            ModInfo.Log.LogInfo($"[Loadout] item {itemId} picked up for {player.NickName} (item ammo now {SafeAmmo(inLevel)})");
            FixAmmo(player, GameDataBlockBase<ItemDataBlock>.GetBlock(itemId)?.inventorySlot ?? InventorySlot.None, custom.ammo, itemId);
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] pick up for {player?.NickName}: {e.Message} — the item stays on the floor next to them"); }
    }

    private static string SafeAmmo(ItemInLevel i) { try { return i.GetCustomData().ammo.ToString("0.##"); } catch { return "?"; } }

    // ---------- ammo of given packs / consumables (fixed by whoever owns the ammo storage) ----------

    private class AmmoFixJob { public SNet_Player Player; public InventorySlot Slot; public float Ammo; public uint Item; public float At, GiveUpAt; }
    private static readonly List<AmmoFixJob> _ammoJobs = new();

    private static void QueueAmmoFix(SNet_Player p, InventorySlot slot, float ammo, uint item) =>
        _ammoJobs.Add(new AmmoFixJob { Player = p, Slot = slot, Ammo = ammo, Item = item, At = Time.time + 0.7f, GiveUpAt = Time.time + 8f });

    private static void OnAmmoFixReceived(ulong sender, byte[] b)
    {
        if (b.Length < 9 || !SNet.HasLocalPlayer) return;
        QueueAmmoFix(SNet.LocalPlayer, (InventorySlot)b[0], BitConverter.ToSingle(b, 1), BitConverter.ToUInt32(b, 5));
    }

    private static void AmmoFixTick()
    {
        for (int i = _ammoJobs.Count - 1; i >= 0; i--)
        {
            var j = _ammoJobs[i];
            if (Time.time < j.At) continue;
            bool done = false;
            try { done = TryFixAmmo(j); } catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] ammo fix: {e.Message}"); done = true; }
            if (done || Time.time > j.GiveUpAt)
            {
                if (!done) ModInfo.Log.LogWarning($"[Loadout] ammo fix for item {j.Item}: the item never showed up in {j.Slot}");
                _ammoJobs.RemoveAt(i);
            }
            else j.At = Time.time + 0.5f;
        }
    }

    private static bool TryFixAmmo(AmmoFixJob j)
    {
        if (!PlayerBackpackManager.TryGetBackpack(j.Player, out var bp) || bp == null || bp.AmmoStorage == null) return false;
        if (!PlayerBackpackManager.TryGetItem(j.Player, j.Slot, out var bpItem) || bpItem == null) return false; // not in the backpack yet
        var st = bp.AmmoStorage;
        var type = PlayerAmmoStorage.GetAmmoTypeFromSlot(j.Slot);
        float before = st.GetAmmoInPack(type);
        if (before + 0.01f < j.Ammo)
        {
            st.SetAmmo(type, j.Ammo);
            st.UpdateAllAmmoUI();
            if (j.Player.IsLocal) PlayerBackpackManager.ForceLocalAmmoStorageUpdate();
        }
        ModInfo.Log.LogInfo($"[Loadout] {j.Player.NickName}: {j.Slot} ({type}) ammo {before:0.##} → {st.GetAmmoInPack(type):0.##}, bullets {st.GetBulletsInPack(type)}, cap {st.GetBulletMaxCap(type)}");
        return true;
    }

    internal static bool GadgetKeyDown() => Enum.TryParse<KeyCode>(_gadgetKey.Value, true, out var key) && Input.GetKeyDown(key);

    /// <summary>Don't react to keys while the player types in chat.</summary>
    internal static bool ChatOpen()
    {
        try { return PlayerChatManager.InChatMode; } catch { return false; }
    }
}
