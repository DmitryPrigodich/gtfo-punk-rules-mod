using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using GameData;
using Gear;
using GtfoPunk.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Localization;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// «Гараж»: новое снаряжение собирается из штатного. Снаряжение в игре — набор деталей (Comps: c = тип детали, v = ID),
/// поэтому можно взять пушку, подменить ей категорию (свои статы/поведение) и детали (ствол, глушитель…) от другой.
/// Модули подписываются на <see cref="OnInject"/>, который вызывается один раз: после загрузки данных игры,
/// но до того, как игра разложит офлайн-снаряжение по слотам.
/// ID выдаются детерминированно, поэтому у всех игроков с модом они совпадают (снаряжение передаётся по сети деталями).
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
        if (GameDataInit.IsLoaded) Run();
    }

    private static void Run()
    {
        if (_done || OnInject == null) return;
        _done = true;
        foreach (Action a in OnInject.GetInvocationList())
        {
            try { a(); } catch (Exception e) { ModInfo.Log.LogError($"[Garage] {e}"); }
        }
        try { Dump(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Garage] список снаряжения не записан: {e.Message}"); }
    }

    // ---------- поиск ----------

    public static string NameOf(PlayerOfflineGearDataBlock b)
    {
        try { return (string)JsonNode.Parse(b.GearJSON)?["Name"] ?? b.name; } catch { return b.name; }
    }

    private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Офлайн-снаряжение по имени (без учёта регистра и пробелов; сначала точное совпадение, потом «содержит»).</summary>
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

    // ---------- детали ----------

    /// <summary>Детали снаряжения: тип детали → ID.</summary>
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

    /// <summary>Разбор строки «тип=ID, тип=Имя пушки, тип=-» в замену деталей.</summary>
    public static Dictionary<int, uint?> ParseParts(string spec, string who)
    {
        var res = new Dictionary<int, uint?>();
        foreach (var item in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = item.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2 || !int.TryParse(kv[0], out int c)) { ModInfo.Log.LogWarning($"[{who}] не понял деталь «{item}»"); continue; }
            if (kv[1] == "-") { res[c] = null; continue; }
            if (uint.TryParse(kv[1], out uint id)) { res[c] = id; continue; }
            var donor = FindByName(kv[1]);
            if (donor == null) { ModInfo.Log.LogWarning($"[{who}] пушка «{kv[1]}» не найдена"); continue; }
            var dc = GetComps(donor.GearJSON);
            res[c] = dc.TryGetValue(c, out var v) ? v : null;
            ModInfo.Log.LogInfo($"[{who}] деталь {c} взята у «{NameOf(donor)}»: {(dc.ContainsKey(c) ? v.ToString() : "нет — убрана")}");
        }
        return res;
    }

    private static string Key(int i) => i < 26 ? ((char)('a' + i)).ToString() : "a" + ((char)('a' + i - 26));

    // ---------- создание ----------

    public static uint FreeId<T>(uint start, uint max) where T : GameDataBlockBase<T>
    {
        for (uint id = start; id < max; id++) if (!GameDataBlockBase<T>.HasBlock(id)) return id;
        throw new Exception($"нет свободного ID для {typeof(T).Name}");
    }

    public static LocalizedText Text(string s)
    {
        // Конструктор LocalizedText(string) в interop-обёртке падает, поэтому заполняем поля вручную.
        var t = new LocalizedText();
        t.Id = 0;
        t.UntranslatedText = s;
        return t;
    }

    /// <summary>Копия категории под новым ID (ID должен влезать в ushort).</summary>
    public static uint CloneCategory(uint srcId, string key, string title, string desc)
    {
        if (GameDataBlockBase<GearCategoryDataBlock>.HasBlock(key)) return GameDataBlockBase<GearCategoryDataBlock>.GetBlockID(key);
        var src = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(srcId) ?? throw new Exception($"нет категории {srcId}");
        var cat = CloneBlock(src);
        uint id = FreeId<GearCategoryDataBlock>(4200, 60000);
        cat.persistentID = id;
        cat.name = key;
        cat.PublicName = Text(title);
        if (desc != null) cat.Description = Text(desc);
        GameDataBlockBase<GearCategoryDataBlock>.AddBlock(cat);
        return id;
    }

    /// <summary>
    /// Новое офлайн-снаряжение из штатного. <paramref name="comps"/>: тип детали → ID (null — убрать деталь).
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
        gear.persistentID = FreeId<PlayerOfflineGearDataBlock>(4200, 1_000_000);
        gear.name = key;
        gear.GearJSON = json.ToJsonString();
        GameDataBlockBase<PlayerOfflineGearDataBlock>.AddBlock(gear);
        ModInfo.Log.LogInfo($"[Garage] добавлено: {title} (снаряжение {gear.persistentID}, на основе «{NameOf(baseGear)}»)");
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

    // ---------- справка ----------

    /// <summary>Имя детали по типу и ID (для справочного файла).</summary>
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
    /// Пишет BepInEx/config/GtfoPunk.GearList.txt — всё снаряжение с деталями (ID и имя детали),
    /// и GtfoPunk.GearParts.txt — все детали каждого типа (имя, модель) и предметы. По ним собираются новые пушки.
    /// </summary>
    private static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Всё офлайн-снаряжение игры (генерирует GtfoPunk Rules при запуске). Детали: тип=ID(имя)");
        sb.AppendLine("# Типы: " + string.Join(", ", Enum.GetValues(typeof(eGearComponent)).Cast<eGearComponent>().Select(e => $"{(int)e}={e}")));
        foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
        {
            if (b == null || string.IsNullOrEmpty(b.GearJSON)) continue;
            var comps = GetComps(b.GearJSON);
            sb.AppendLine($"{b.persistentID,6}  {NameOf(b),-28} [{b.Type}] {string.Join(" ", comps.OrderBy(k => k.Key).Select(k => { var n = PartName(k.Key, k.Value); return n == null ? $"{k.Key}={k.Value}" : $"{k.Key}={k.Value}({n})"; }))}");
        }
        File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.GearList.txt"), sb.ToString());

        sb.Clear();
        sb.AppendLine("# Детали оружия и инструментов: ID  имя  публичное имя  модель");
        DumpParts<GearFrontPartDataBlock>(sb, "12 FrontPart (ствол/перед)", b => b.General);
        DumpParts<GearPartAttachmentDataBlock>(sb, "13/14/17/29/35 Attachment (насадки)", b => b.General, b => $"→ {b.AttachToAlign}");
        DumpParts<GearReceiverPartDataBlock>(sb, "16 ReceiverPart (ствольная коробка)", b => b.General);
        DumpParts<GearStockPartDataBlock>(sb, "19 StockPart (приклад)", b => b.General);
        DumpParts<GearSightPartDataBlock>(sb, "21 SightPart (прицел)", b => b.General);
        DumpParts<GearMagPartDataBlock>(sb, "23 MagPart (магазин)", b => b.General);
        DumpParts<GearFlashlightPartDataBlock>(sb, "25 FlashlightPart (фонарь)", b => b.General);
        sb.AppendLine().AppendLine("## Категории (2): ID  имя  публичное имя");
        foreach (var c in GameDataBlockBase<GearCategoryDataBlock>.GetAllBlocks())
            if (c != null) sb.AppendLine($"{c.persistentID,5}  {c.name,-40} {(string)c.PublicName}");
        sb.AppendLine().AppendLine("## Предметы (ItemDataBlock): ID  имя  публичное имя");
        foreach (var i in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
            if (i != null) sb.AppendLine($"{i.persistentID,5}  {i.name,-40} {i.publicName}");
        File.WriteAllText(Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.GearParts.txt"), sb.ToString());
    }
}
