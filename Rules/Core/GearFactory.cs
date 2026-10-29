using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using GameData;
using Gear;
using Il2CppInterop.Runtime.InteropTypes;
using Localization;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// "Garage": new gear is assembled from stock gear. Gear in the game is a set of parts (Comps: c = part type, v = ID),
/// so you can take a gun, swap its category (own stats/behaviour) and parts (barrel, silencer…) from another one.
/// Modules subscribe to <see cref="OnInject"/>, which is called once: after the game data is loaded,
/// but before the game distributes offline gear into slots.
/// IDs are assigned deterministically, so they match for all players with the mod (gear is sent over the network as parts).
/// </summary>
public static class GearFactory
{
    public static event Action OnInject;
    private static bool _done, _patched;

    public static void Install()
    {
        if (_patched) return;
        _patched = true;
        var h = new Harmony(ModInfo.Guid + ".GearFactory");
        h.Patch(AccessTools.Method(typeof(GameDataInit), nameof(GameDataInit.Initialize)), postfix: new HarmonyMethod(typeof(GearFactory), nameof(Run)));
        h.Patch(AccessTools.Method(typeof(GearManager), nameof(GearManager.LoadOfflineGearDatas)), prefix: new HarmonyMethod(typeof(GearFactory), nameof(Run)));
        try
        {
            h.Patch(AccessTools.Method(typeof(Player.PlayerBackpackManager), nameof(Player.PlayerBackpackManager.EquipBotGear), new[] { typeof(SNetwork.SNet_Player), typeof(GearIDRange) }),
                prefix: new HarmonyMethod(typeof(GearFactory), nameof(GuardBotGear)));
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[GearFactory] bot gear guard not installed: {e.Message}"); }
        if (GameDataInit.IsLoaded) Run();
    }

    // ---------- stable IDs ----------

    /// <summary>
    /// Fixed IDs for everything the garage has ever added. Gear travels over the network and is saved in favorites
    /// by category/gear ID, so an ID must never move to another item, and removing an item must not shift the others.
    /// New keys get a deterministic ID from their name (see <see cref="StableId"/>).
    /// </summary>
    private static readonly Dictionary<string, uint> FixedIds = new()
    {
        ["GtfoPunk_SilencedGun"] = 4200,          // category
        ["GtfoPunk_SilencedGun_0"] = 4200,        // gear
        ["GtfoPunk_FogTurret"] = 4201,
        ["GtfoPunk_LightTurret"] = 4202,
        ["GtfoPunk_FogTurbineTurret"] = 4203,
        ["GtfoPunk_TrackerTurret"] = 4204,
        ["GtfoPunk_FoamTurret"] = 4205,
        ["GtfoPunk_GrenadeTurret"] = 4206,
        // 4207, 4208 — retired (test LT2-B/C in 0.4.2–0.4.3); kept as aliases, never reused.
        ["GtfoPunk_LightTurret_Retired4207"] = 4207,
        ["GtfoPunk_LightTurret_Retired4208"] = 4208,
    };

    /// <summary>ID for a key: from the fixed table, otherwise a hash of the name in 4300–9999 (probing on collision).</summary>
    public static uint StableId<T>(string key) where T : GameDataBlockBase<T>
    {
        if (FixedIds.TryGetValue(key, out var fixedId)) return fixedId;
        uint h = 2166136261;
        foreach (char ch in key) { h ^= ch; h *= 16777619; }
        uint id = 4300 + h % 5700;
        for (int i = 0; i < 5700 && (GameDataBlockBase<T>.HasBlock(id) || FixedIds.ContainsValue(id)); i++) id = 4300 + (id - 4300 + 1) % 5700;
        return id;
    }

    // ---------- broken favorites guard ----------

    /// <summary>
    /// A bot's saved favorite may reference a category that no longer exists (an item removed from the mod).
    /// The game then fails to assemble the gear and the bot can't spawn (and the mission can't start).
    /// Replace such a tool with the stock sentry instead.
    /// </summary>
    private static void GuardBotGear(ref GearIDRange gearSetup)
    {
        try
        {
            if (gearSetup == null) return;
            uint cat = gearSetup.GetCompID(eGearComponent.Category);
            if (cat == 0 || GameDataBlockBase<GearCategoryDataBlock>.HasBlock(cat)) return;
            var sentry = FindByCategory(12, b => NameOf(b).IndexOf("Burst", StringComparison.OrdinalIgnoreCase) >= 0) ?? FindByCategory(12);
            if (sentry == null) return;
            ModInfo.Log.LogWarning($"[GearFactory] bot gear with missing category {cat} replaced by '{NameOf(sentry)}'");
            gearSetup = new GearIDRange(sentry.GearJSON);
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[GearFactory] bot gear guard: {e.Message}"); }
    }

    private static void Run()
    {
        if (_done || OnInject == null) return;
        _done = true;
        foreach (Action a in OnInject.GetInvocationList())
        {
            try { a(); } catch (Exception e) { ModInfo.Log.LogError($"[GearFactory] {e}"); }
        }
        try { Dump(); } catch (Exception e) { ModInfo.Log.LogWarning($"[GearFactory] gear list not written: {e.Message}"); }
    }

    // ---------- lookup ----------

    public static string NameOf(PlayerOfflineGearDataBlock b)
    {
        try { return (string)JsonNode.Parse(b.GearJSON)?["Name"] ?? b.name; } catch { return b.name; }
    }

    private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Offline gear by name (ignoring case and spaces; exact match first, then "contains").</summary>
    public static PlayerOfflineGearDataBlock FindByName(string name)
    {
        string n = Norm(name);
        PlayerOfflineGearDataBlock partial = null;
        foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
        {
            if (b == null || string.IsNullOrEmpty(b.GearJSON)) continue;
            string bn = Norm(NameOf(b));
            if (bn == n) return b;
            if (partial == null && bn.Contains(n)) partial = b;
        }
        return partial;
    }

    public static PlayerOfflineGearDataBlock FindByCategory(uint categoryId, Func<PlayerOfflineGearDataBlock, bool> prefer = null)
    {
        PlayerOfflineGearDataBlock first = null;
        foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
        {
            if (b == null || string.IsNullOrEmpty(b.GearJSON)) continue;
            if (GetComps(b.GearJSON).GetValueOrDefault((int)eGearComponent.Category) != categoryId) continue;
            if (prefer == null || prefer(b)) return b;
            first ??= b;
        }
        return first;
    }

    // ---------- parts ----------

    /// <summary>Gear parts: part type → ID.</summary>
    public static Dictionary<int, uint> GetComps(string gearJson)
    {
        var res = new Dictionary<int, uint>();
        try
        {
            var comps = JsonNode.Parse(gearJson)?["Packet"]?["Comps"]?.AsObject();
            if (comps == null) return res;
            foreach (var kv in comps)
                if (kv.Value is JsonObject o && o["c"] != null && o["v"] != null) res[(int)o["c"]] = (uint)(int)o["v"];
        }
        catch { }
        return res;
    }

    /// <summary>Parse 'type=ID, type=Gun name, type=-' into part replacements.</summary>
    public static Dictionary<int, uint?> ParseParts(string spec, string who)
    {
        var res = new Dictionary<int, uint?>();
        foreach (var item in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !int.TryParse(kv[0], out int c)) { ModInfo.Log.LogWarning($"[{who}] can't parse part '{item}'"); continue; }
            if (kv[1] == "-") { res[c] = null; continue; }
            if (uint.TryParse(kv[1], out uint id)) { res[c] = id; continue; }
            var donor = FindByName(kv[1]);
            if (donor == null) { ModInfo.Log.LogWarning($"[{who}] gun '{kv[1]}' not found"); continue; }
            var dc = GetComps(donor.GearJSON);
            res[c] = dc.TryGetValue(c, out var v) ? v : null;
            ModInfo.Log.LogInfo($"[{who}] part {c} taken from '{NameOf(donor)}': {(dc.ContainsKey(c) ? v.ToString() : "none — removed")}");
        }
        return res;
    }

    private static string Key(int i) => i < 26 ? ((char)('a' + i)).ToString() : "a" + ((char)('a' + i - 26));

    // ---------- creation ----------

    public static uint FreeId<T>(uint start, uint max) where T : GameDataBlockBase<T>
    {
        for (uint id = start; id < max; id++) if (!GameDataBlockBase<T>.HasBlock(id)) return id;
        throw new Exception($"no free ID for {typeof(T).Name}");
    }

    public static LocalizedText Text(string s)
    {
        // The LocalizedText(string) constructor crashes in the interop wrapper, so fill the fields manually.
        var t = new LocalizedText();
        t.Id = 0;
        t.UntranslatedText = s;
        return t;
    }

    /// <summary>Copy of a category under a new ID (the ID must fit into ushort).</summary>
    public static uint CloneCategory(uint srcId, string key, string title, string desc)
    {
        if (GameDataBlockBase<GearCategoryDataBlock>.HasBlock(key)) return GameDataBlockBase<GearCategoryDataBlock>.GetBlockID(key);
        var src = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(srcId) ?? throw new Exception($"no category {srcId}");
        var cat = CloneBlock(src);
        uint id = StableId<GearCategoryDataBlock>(key);
        if (GameDataBlockBase<GearCategoryDataBlock>.HasBlock(id)) throw new Exception($"category ID {id} for {key} is taken");
        cat.persistentID = id;
        cat.name = key;
        cat.PublicName = Text(title);
        if (desc != null) cat.Description = Text(desc);
        GameDataBlockBase<GearCategoryDataBlock>.AddBlock(cat);
        return id;
    }

    /// <summary>Copy of a weapon archetype (damage, rate of fire…) under a stable ID.</summary>
    public static uint CloneArchetype(uint srcId, string key)
    {
        if (GameDataBlockBase<ArchetypeDataBlock>.HasBlock(key)) return GameDataBlockBase<ArchetypeDataBlock>.GetBlockID(key);
        var src = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(srcId) ?? throw new Exception($"no archetype {srcId}");
        var a = CloneBlock(src);
        a.persistentID = StableId<ArchetypeDataBlock>(key);
        a.name = key;
        GameDataBlockBase<ArchetypeDataBlock>.AddBlock(a);
        return a.persistentID;
    }

    /// <summary>
    /// Gives a (cloned) category its own copies of its fire-mode archetypes, named <paramref name="publicName"/> (the
    /// in-game weapon list shows the archetype's name above the gear's), with an optional tweak of each copy.
    /// </summary>
    public static void OwnArchetypes(uint catId, string keyPrefix, string publicName, Action<string, ArchetypeDataBlock> tweak = null)
    {
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(catId);
        if (cat == null) return;
        uint Own(uint src, string mode)
        {
            if (src == 0) return 0;
            uint id = CloneArchetype(src, keyPrefix + "_Arch_" + mode);
            var a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
            if (a != null)
            {
                if (!string.IsNullOrEmpty(publicName)) a.PublicName = Text(publicName);
                tweak?.Invoke(mode, a);
            }
            return id;
        }
        cat.SemiArchetype = Own(cat.SemiArchetype, "Semi");
        cat.BurstArchetype = Own(cat.BurstArchetype, "Burst");
        cat.AutoArchetype = Own(cat.AutoArchetype, "Auto");
        cat.SemiBurstArchetype = Own(cat.SemiBurstArchetype, "SemiBurst");
    }

    /// <summary>
    /// New offline gear from stock gear. <paramref name="comps"/>: part type → ID (null — remove the part).
    /// </summary>
    public static PlayerOfflineGearDataBlock AddGear(PlayerOfflineGearDataBlock baseGear, string key, string title, IDictionary<int, uint?> comps)
    {
        if (GameDataBlockBase<PlayerOfflineGearDataBlock>.HasBlock(key)) return GameDataBlockBase<PlayerOfflineGearDataBlock>.GetBlock(key);

        var json = JsonNode.Parse(baseGear.GearJSON)!.AsObject();
        json["Name"] = title;
        var packet = json["Packet"]!.AsObject();
        if (packet["publicName"] is JsonObject pn) pn["data"] = title;

        var list = GetComps(baseGear.GearJSON).OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var kv in comps)
        {
            if (kv.Value.HasValue) list[kv.Key] = kv.Value.Value;
            else list.Remove(kv.Key);
        }
        var newComps = new JsonObject { ["Length"] = list.Count };
        int i = 0;
        foreach (var kv in list.OrderBy(kv => kv.Key))
            newComps[Key(i++)] = new JsonObject { ["c"] = kv.Key, ["v"] = (int)kv.Value };
        packet["Comps"] = newComps;

        var gear = CloneBlock(baseGear);
        gear.persistentID = StableId<PlayerOfflineGearDataBlock>(key);
        if (GameDataBlockBase<PlayerOfflineGearDataBlock>.HasBlock(gear.persistentID)) throw new Exception($"gear ID {gear.persistentID} for {key} is taken");
        gear.name = key;
        gear.GearJSON = json.ToJsonString();
        GameDataBlockBase<PlayerOfflineGearDataBlock>.AddBlock(gear);
        ModInfo.Log.LogInfo($"[GearFactory] added: {title} (gear {gear.persistentID}, based on '{NameOf(baseGear)}')");
        return gear;
    }

    public static T CloneBlock<T>(T src) where T : Il2CppObjectBase
    {
        var dst = (T)Activator.CreateInstance(typeof(T));
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            if (p.DeclaringType == typeof(Il2CppObjectBase) || p.DeclaringType == typeof(Il2CppSystem.Object)) continue;
            try { p.SetValue(dst, p.GetValue(src)); } catch { }
        }
        return dst;
    }

    // ---------- reference ----------

    /// <summary>Part name by type and ID (for the reference file).</summary>
    private static string PartName(int type, uint id)
    {
        try
        {
            return (eGearComponent)type switch
            {
                eGearComponent.Category => GameDataBlockBase<GearCategoryDataBlock>.GetBlock(id)?.name,
                eGearComponent.FrontPart => GameDataBlockBase<GearFrontPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.FrontPartAttachmentA or eGearComponent.FrontPartAttachmentB or eGearComponent.ReceiverPartAttachment
                    or eGearComponent.ToolMainPartAttachment or eGearComponent.ToolDeliveryPartAttachment => GameDataBlockBase<GearPartAttachmentDataBlock>.GetBlock(id)?.name,
                eGearComponent.ReceiverPart => GameDataBlockBase<GearReceiverPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.StockPart => GameDataBlockBase<GearStockPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.SightPart => GameDataBlockBase<GearSightPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.MagPart => GameDataBlockBase<GearMagPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.FlashlightPart => GameDataBlockBase<GearFlashlightPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolMainPart => GameDataBlockBase<GearToolMainPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolGripPart => GameDataBlockBase<GearToolGripPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolDeliveryPart => GameDataBlockBase<GearToolDeliveryPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolPayloadPart => GameDataBlockBase<GearToolPayloadPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolTargetingPart => GameDataBlockBase<GearToolTargetingPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.ToolScreenPart => GameDataBlockBase<GearToolScreenPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.MeleeHeadPart => GameDataBlockBase<GearMeleeHeadPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.MeleeNeckPart => GameDataBlockBase<GearMeleeNeckPartDataBlock>.GetBlock(id)?.name,
                eGearComponent.MeleeHandlePart => GameDataBlockBase<GearMeleeHandlePartDataBlock>.GetBlock(id)?.name,
                eGearComponent.MeleePommelPart => GameDataBlockBase<GearMeleePommelPartDataBlock>.GetBlock(id)?.name,
                _ => null,
            };
        }
        catch { return null; }
    }

    private static void DumpParts<T>(StringBuilder sb, string title, Func<T, GearPartGeneralData> general, Func<T, string> extra = null) where T : GameDataBlockBase<T>
    {
        sb.AppendLine().AppendLine($"## {title}");
        foreach (var b in GameDataBlockBase<T>.GetAllBlocks())
        {
            if (b == null) continue;
            string model = "", pub = "";
            try { var g = general(b); model = g?.Model ?? ""; pub = g?.PublicName ?? ""; } catch { }
            model = Path.GetFileNameWithoutExtension(model);
            sb.AppendLine($"{b.persistentID,5}  {b.name,-40} {pub,-24} {model} {extra?.Invoke(b)}");
        }
    }

    /// <summary>
    /// Writes BepInEx/config/GtfoPunk.GearList.txt — all gear with parts (part ID and name),
    /// and GtfoPunk.GearParts.txt — all parts of each type (name, model) and items. Use them to assemble new guns.
    /// </summary>
    private static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# All offline gear of the game (generated by GtfoPunk Rules at startup). Parts: type=ID(name)");
        sb.AppendLine("# Types: " + string.Join(", ", Enum.GetValues(typeof(eGearComponent)).Cast<eGearComponent>().Select(e => $"{(int)e}={e}")));
        foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
        {
            if (b == null || string.IsNullOrEmpty(b.GearJSON)) continue;
            var comps = GetComps(b.GearJSON);
            sb.AppendLine($"{b.persistentID,6}  {NameOf(b),-28} [{b.Type}] {string.Join(" ", comps.OrderBy(k => k.Key).Select(k => { var n = PartName(k.Key, k.Value); return n == null ? $"{k.Key}={k.Value}" : $"{k.Key}={k.Value}({n})"; }))}");
        }
        File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.GearList.txt"), sb.ToString());

        sb.Clear();
        sb.AppendLine("# Weapon and tool parts: ID  name  public name  model");
        DumpParts<GearFrontPartDataBlock>(sb, "12 FrontPart (barrel/front)", b => b.General);
        DumpParts<GearPartAttachmentDataBlock>(sb, "13/14/17/29/35 Attachment", b => b.General, b => $"→ {b.AttachToAlign}");
        DumpParts<GearReceiverPartDataBlock>(sb, "16 ReceiverPart", b => b.General);
        DumpParts<GearStockPartDataBlock>(sb, "19 StockPart", b => b.General);
        DumpParts<GearSightPartDataBlock>(sb, "21 SightPart", b => b.General);
        DumpParts<GearMagPartDataBlock>(sb, "23 MagPart", b => b.General);
        DumpParts<GearFlashlightPartDataBlock>(sb, "25 FlashlightPart", b => b.General);
        DumpParts<GearToolMainPartDataBlock>(sb, "27 ToolMainPart (tool base)", b => b.General);
        DumpParts<GearToolGripPartDataBlock>(sb, "30 ToolGripPart", b => b.General);
        DumpParts<GearToolScreenPartDataBlock>(sb, "42 ToolScreenPart", b => b.General);
        DumpTyped(sb, "33 ToolDeliveryPart (subtype = 32)", (GearToolDeliveryPartDataBlock b) => b.GeneralDatas, d => ((int)d.Type, d.Data));
        DumpTyped(sb, "37 ToolPayloadPart (subtype = 36)", (GearToolPayloadPartDataBlock b) => b.GeneralDatas, d => ((int)d.Type, d.Data));
        DumpTyped(sb, "40 ToolTargetingPart (subtype = 39)", (GearToolTargetingPartDataBlock b) => b.GeneralDatas, d => ((int)d.Type, d.Data));
        sb.AppendLine().AppendLine("## Categories (2): ID  name  public name");
        foreach (var c in GameDataBlockBase<GearCategoryDataBlock>.GetAllBlocks())
            if (c != null) sb.AppendLine($"{c.persistentID,5}  {c.name,-40} {(string)c.PublicName}");
        sb.AppendLine().AppendLine("## Items (ItemDataBlock): ID  name  public name");
        foreach (var i in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
            if (i != null) sb.AppendLine($"{i.persistentID,5}  {i.name,-40} {i.publicName}");
        File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.GearParts.txt"), sb.ToString());
    }

    private static bool _flashlightsDumped;

    /// <summary>
    /// Once, in the lobby (gear part models are loaded there): BepInEx/config/GtfoPunk.Flashlights.txt — every flashlight
    /// setting (range, angle, brightness, colour), which flashlight part model uses which setting, and which gun has which part.
    /// </summary>
    public static void DumpFlashlights()
    {
        if (_flashlightsDumped) return;
        _flashlightsDumped = true;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Flashlight settings (FlashlightSettingsDataBlock): ID  name  range m  angle °  intensity  colour  cookie");
            foreach (var f in GameDataBlockBase<FlashlightSettingsDataBlock>.GetAllBlocks())
                if (f != null) sb.AppendLine($"{f.persistentID,5}  {f.name,-32} {f.range,6:0.##} {f.angle,6:0.#} {f.intensity,7:0.###}  ({f.color.r:0.##},{f.color.g:0.##},{f.color.b:0.##})  {f.cookie}");

            // Flashlight part models → their setting.
            var settingOf = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            sb.AppendLine().AppendLine("# Flashlight part models in memory: model  → setting ID");
            foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Gear.GearPartFlashlight>()))
            {
                var fp = o?.TryCast<Gear.GearPartFlashlight>();
                if (fp == null) continue;
                string n = fp.gameObject.name.Replace("(Clone)", "").Trim();
                if (settingOf.ContainsKey(n)) continue;
                settingOf[n] = fp.m_settingsID;
                sb.AppendLine($"  {n,-24} → {fp.m_settingsID}");
            }

            sb.AppendLine().AppendLine("# Guns: name  → flashlight part (25)  → setting");
            foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
            {
                if (b == null || string.IsNullOrEmpty(b.GearJSON)) continue;
                var comps = GetComps(b.GearJSON);
                if (!comps.TryGetValue((int)eGearComponent.FlashlightPart, out var part)) continue;
                var pb = GameDataBlockBase<GearFlashlightPartDataBlock>.GetBlock(part);
                string model = pb?.General?.Model == null ? "?" : Path.GetFileNameWithoutExtension(pb.General.Model);
                string set = settingOf.TryGetValue(model, out var sid) ? sid.ToString() : "?";
                sb.AppendLine($"  {NameOf(b),-28} part {part} ({pb?.name}, model {model}) → setting {set}");
            }
            File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.Flashlights.txt"), sb.ToString());
            ModInfo.Log.LogInfo($"[GearFactory] flashlights written: {settingOf.Count} part models → GtfoPunk.Flashlights.txt");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[GearFactory] flashlights dump: {e.Message}"); }
    }

    private static void DumpTyped<T, TItem>(StringBuilder sb, string title, Func<T, Il2CppSystem.Collections.Generic.List<TItem>> list, Func<TItem, (int type, GearPartGeneralData data)> get) where T : GameDataBlockBase<T>
    {
        sb.AppendLine().AppendLine("## " + title);
        foreach (var b in GameDataBlockBase<T>.GetAllBlocks())
        {
            if (b == null) continue;
            var l = list(b);
            if (l == null) continue;
            foreach (var item in l)
            {
                var (type, data) = get(item);
                if (data == null) continue;
                sb.AppendLine($"{b.persistentID,5}/{type,-2} {b.name,-40} {data.PublicName,-24} {data.Model}");
            }
        }
    }
}
