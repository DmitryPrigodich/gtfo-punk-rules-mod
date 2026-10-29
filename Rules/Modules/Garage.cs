using GameData;
using Gear;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The garage — a weapon fitting room in the lobby (stage 1: parts). A test gun "DRED GARAGE PROTOTYPE" is added to
/// the main-slot weapons; equip it, press the garage key (F8) and step through its parts — front, receiver, stock,
/// sight, magazine, flashlight — ↑↓ picks a part, &lt; &gt; (comma, period) change it (← → move the lobby camera):
/// the gun in your hands in the lobby is rebuilt at once.
/// The combination is remembered in [Garage] Prototype (the same format as SilencedGun.MainGuns), and Enter writes a
/// ready config line to BepInEx/config/GtfoPunk.Garage.txt.
/// The prototype is gear like any other: everyone needs the mod (the others build it from your parts too).
/// </summary>
public class Garage : ModuleBase
{
    public override string Id => "Garage";
    public override string Description => "Weapon fitting room in the lobby: equip DRED GARAGE PROTOTYPE, press F8, ↑↓ — part, < > — change it. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<KeyCode> _key;
    private static ConfigEntry<string> _proto;

    private const string ProtoName = "DRED GARAGE PROTOTYPE";
    private const string ProtoBody = "Van Auken LTC5";

    protected override void OnBindConfig()
    {
        _key = Bind("Key", KeyCode.F8, "Opens / closes the garage panel in the lobby.");
        _proto = Bind("Prototype", ProtoBody + " + 12=15, 19=3, 21=10, 23=1 | type=Prototype",
            "The prototype's parts (written by the garage): 'Body + part=ID, … | options' — as in SilencedGun.MainGuns. Applied after a game restart; in the lobby the garage changes the gun at once.");
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
    }

    private static uint _protoCat;

    private static void Inject()
    {
        try { _protoCat = SilencedGun.AddGun(ProtoName + ": " + _proto.Value, silent: false, "GtfoPunk_Garage_", ProtoBody, "Prototype", sound: null); }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Garage] prototype: {e.Message}"); }
        if (_protoCat != 0) ModInfo.Log.LogInfo($"[Garage] prototype ready (category {_protoCat})");
    }

    // ---------- the rows: part types and their candidates ----------

    private class Row
    {
        public eGearComponent Comp;
        public string En, Ru;
        public List<(uint Id, string Name)> Parts;
    }

    private static List<Row> _rows;
    private static Dictionary<(int, uint), List<string>> _usedBy;

    private static void BuildRows()
    {
        if (_rows != null) return;
        _usedBy = new();
        foreach (var b in GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks())
        {
            if (b == null || string.IsNullOrEmpty(b.GearJSON) || (b.name ?? "").StartsWith("GtfoPunk_")) continue;
            foreach (var kv in GearFactory.GetComps(b.GearJSON))
            {
                if (!_usedBy.TryGetValue((kv.Key, kv.Value), out var l)) _usedBy[(kv.Key, kv.Value)] = l = new();
                string n = GearFactory.NameOf(b);
                if (!l.Contains(n)) l.Add(n);
            }
        }
        static List<(uint, string)> Of<T>(Func<string, bool> keep) where T : GameDataBlockBase<T>
            => GameDataBlockBase<T>.GetAllBlocks().Where(x => x != null && keep(x.name ?? "")).OrderBy(x => x.name).Select(x => (x.persistentID, x.name)).ToList();
        bool NotTool(string n) => !n.Contains("Mapper") && !n.Contains("MotionTracker") && !n.StartsWith("Melee") && !n.StartsWith("Gluegun");
        _rows = new()
        {
            new Row { Comp = eGearComponent.FrontPart, En = "Front", Ru = "Нос", Parts = Of<GearFrontPartDataBlock>(n => n.StartsWith("Front_")) },
            new Row { Comp = eGearComponent.ReceiverPart, En = "Receiver", Ru = "Коробка", Parts = Of<GearReceiverPartDataBlock>(NotTool) },
            new Row { Comp = eGearComponent.StockPart, En = "Stock", Ru = "Приклад", Parts = Of<GearStockPartDataBlock>(NotTool) },
            new Row { Comp = eGearComponent.SightPart, En = "Sight", Ru = "Прицел", Parts = Of<GearSightPartDataBlock>(_ => true) },
            new Row { Comp = eGearComponent.MagPart, En = "Magazine", Ru = "Магазин", Parts = Of<GearMagPartDataBlock>(_ => true) },
            new Row { Comp = eGearComponent.FlashlightPart, En = "Flashlight", Ru = "Фонарь", Parts = Of<GearFlashlightPartDataBlock>(_ => true) },
        };
        ModInfo.Log.LogInfo($"[Garage] parts: {string.Join(", ", _rows.Select(r => $"{r.En} {r.Parts.Count}"))}");
    }

    // ---------- input ----------

    private static bool _open, _hooked;
    private static int _row;
    private static string _status = "";
    private static int _missLogs;

    private static void Tick()
    {
        bool lobby = GameStateManager.CurrentStateName == eGameStateName.Lobby;
        if (!lobby) { _open = false; return; }
        if (Input.GetKeyDown(_key.Value))
        {
            _open = !_open;
            if (_open)
            {
                BuildRows();
                if (!_hooked) { _hooked = true; Overlay.Ensure(); Overlay.OnDraw += Draw; }
            }
        }
        if (!_open) return;
        if (Input.GetKeyDown(KeyCode.UpArrow)) _row = (_row + _rows.Count - 1) % _rows.Count;
        if (Input.GetKeyDown(KeyCode.DownArrow)) _row = (_row + 1) % _rows.Count;
        // ← → move the lobby camera — the parts change with , and . (the < > keys)
        if (Input.GetKeyDown(KeyCode.Comma)) Step(-1);
        if (Input.GetKeyDown(KeyCode.Period)) Step(+1);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) WriteLine();
    }

    /// <summary>The prototype in the local player's main slot (null if another gun is equipped there): the backpack's
    /// gear, or else the gear the game remembers as equipped in the slot.</summary>
    private static GearIDRange Prototype()
    {
        GearIDRange g = null;
        try
        {
            var bp = PlayerBackpackManager.LocalBackpack;
            if (bp != null && bp.TryGetBackpackItem(InventorySlot.GearStandard, out BackpackItem item)) g = item?.GearIDRange;
        }
        catch { }
        if (g == null || g.GetCompID(eGearComponent.Category) != _protoCat)
            try { g = GearManager.Current?.m_lastEquippedGearPerSlot?[(int)InventorySlot.GearStandard]; } catch { g = null; }
        return g != null && g.GetCompID(eGearComponent.Category) == _protoCat ? g : null;
    }

    private static void Step(int dir)
    {
        var gear = Prototype();
        if (gear == null)
        {
            _status = Lang.T($"Equip {ProtoName} in the main slot", $"Возьми {ProtoName} в основной слот");
            if (_missLogs++ < 3) ModInfo.Log.LogInfo($"[Garage] no prototype in the main slot (category {_protoCat})");
            return;
        }
        var row = _rows[_row];
        if (row.Parts.Count == 0) return;
        uint cur = gear.GetCompID(row.Comp);
        int i = row.Parts.FindIndex(p => p.Id == cur);
        i = i < 0 ? 0 : (i + dir + row.Parts.Count) % row.Parts.Count;
        uint id = row.Parts[i].Id;
        try
        {
            gear.SetCompID(row.Comp, id);
            PlayerBackpackManager.EquipLocalGear(gear);
            foreach (var bar in CellMenu.CM_PlayerLobbyBar.instances) // the lobby redraws the gun
                if (bar?.m_player != null && bar.m_player.IsLocal) bar.m_updateInventory = true;
            Remember(gear);
            _status = $"{row.En}: {row.Parts[i].Name} ({id})";
            ModInfo.Log.LogInfo($"[Garage] {row.En} → {row.Parts[i].Name} ({id})");
        }
        catch (Exception e) { _status = e.Message; ModInfo.Log.LogWarning($"[Garage] {e.Message}"); }
    }

    /// <summary>The parts into [Garage] Prototype (options after '|' are kept).</summary>
    private static void Remember(GearIDRange gear)
    {
        string opts = _proto.Value.Contains('|') ? " " + _proto.Value.Substring(_proto.Value.IndexOf('|')) : "";
        _proto.Value = ProtoBody + " + " + string.Join(", ", _rows.Select(r => $"{(int)r.Comp}={gear.GetCompID(r.Comp)}")) + opts;
    }

    private static void WriteLine()
    {
        var gear = Prototype();
        if (gear == null) { _status = Lang.T($"Equip {ProtoName} in the main slot", $"Возьми {ProtoName} в основной слот"); return; }
        Remember(gear);
        string line = $"NEW GUN: {_proto.Value}";
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.Garage.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm}  {line}\n");
            _status = Lang.T("Written to GtfoPunk.Garage.txt", "Записано в GtfoPunk.Garage.txt");
            ModInfo.Log.LogInfo($"[Garage] {line}");
        }
        catch (Exception e) { _status = e.Message; }
    }

    // ---------- drawing ----------

    private static GUIStyle _style, _small;
    private static Texture2D _white;

    private static void Draw()
    {
        if (!_open || _rows == null) return;
        _white ??= MakeWhite();
        float H = Screen.height, line = H * 0.032f, w = H * 0.62f, x = H * 0.03f, y = H * 0.16f;
        _style ??= new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false, clipping = TextClipping.Clip };
        _small ??= new GUIStyle(_style);
        _style.fontSize = Mathf.RoundToInt(line * 0.6f);
        _small.fontSize = Mathf.RoundToInt(line * 0.45f);
        var gear = Prototype();
        float h = line * (3 + _rows.Count * 1.6f + 2);
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f); GUI.DrawTexture(new Rect(x, y, w, h), _white);
        GUI.color = new Color(1f, 0.72f, 0.3f, 1f);
        GUI.Label(new Rect(x + line * 0.5f, y + line * 0.3f, w, line), Lang.T("GARAGE — ↑↓ part, < > change, Enter — write, F8 — close", "ГАРАЖ — ↑↓ деталь, < > (Б Ю) сменить, Enter — записать, F8 — закрыть"), _style);
        float cy = y + line * 1.5f;
        if (gear == null)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f, 1f);
            GUI.Label(new Rect(x + line * 0.5f, cy, w, line), Lang.T($"Equip {ProtoName} in the main slot", $"Возьми {ProtoName} в основной слот"), _style);
            cy += line * 1.2f;
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            uint cur = gear?.GetCompID(r.Comp) ?? 0;
            var part = r.Parts.FirstOrDefault(p => p.Id == cur);
            string name = part.Name ?? (cur == 0 ? "—" : cur.ToString());
            bool sel = i == _row;
            GUI.color = sel ? new Color(1f, 0.85f, 0.5f, 1f) : new Color(0.8f, 0.9f, 0.95f, 1f);
            GUI.Label(new Rect(x + line * 0.5f, cy, w, line), $"{(sel ? "▶ " : "   ")}{Lang.T(r.En, r.Ru),-10} ◀ {name} ({cur}) ▶", _style);
            cy += line;
            string used = _usedBy.TryGetValue(((int)r.Comp, cur), out var l) && l.Count > 0
                ? Lang.T("on: ", "на: ") + string.Join(", ", l.Take(4)) + (l.Count > 4 ? $" +{l.Count - 4}" : "")
                : Lang.T("unused in the game", "в игре не используется");
            GUI.color = new Color(0.6f, 0.7f, 0.75f, 1f);
            GUI.Label(new Rect(x + line * 1.6f, cy - line * 0.15f, w, line), used, _small);
            cy += line * 0.6f;
        }
        GUI.color = new Color(0.8f, 0.9f, 0.95f, 1f);
        GUI.Label(new Rect(x + line * 0.5f, cy + line * 0.3f, w, line), _status, _small);
        GUI.color = old;
    }

    private static Texture2D MakeWhite()
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        return t;
    }
}
