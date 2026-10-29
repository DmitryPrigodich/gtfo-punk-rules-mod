using GameData;
using Gear;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The garage — a weapon fitting room in the lobby (stage 1: parts). A test gun "DRED GARAGE PROTOTYPE" is added to
/// the main-slot weapons; the garage key (F8) puts it in your hands and opens the panel: W S (↑↓) picks a part — front,
/// receiver, stock, sight, magazine, flashlight — A D (or , .) change it (← → move the lobby camera); the gun in your
/// hands in the lobby is rebuilt at once.
/// The combination is remembered in [Garage] Prototype (the same format as SilencedGun.MainGuns), and Enter writes a
/// ready config line to BepInEx/config/GtfoPunk.Garage.txt.
/// The prototype is gear like any other: everyone needs the mod (the others build it from your parts too).
/// </summary>
public class Garage : ModuleBase
{
    public override string Id => "Garage";
    public override string Description => "Weapon fitting room in the lobby: F8 — DRED GARAGE PROTOTYPE in your hands, W S — part, A D — change it. Everyone needs it.";
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
        if (!lobby) { if (_open) ClosePreview(); _open = false; return; }
        if (Input.GetKeyDown(_key.Value))
        {
            _open = !_open;
            if (_open)
            {
                BuildRows();
                var g = Prototype();
                if (g != null) { try { Equip(g); Rebuild(g); _status = ""; } catch (Exception e) { _status = e.Message; } }
                if (!_hooked) { _hooked = true; Overlay.Ensure(); Overlay.OnDraw += Draw; }
            }
        }
        if (!_open) { ClosePreview(); return; }
        PreviewTick();
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) _row = (_row + _rows.Count - 1) % _rows.Count;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) _row = (_row + 1) % _rows.Count;
        // ← → move the lobby camera — the parts change with A / D (or , . — the < > keys)
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Comma)) Step(-1);
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.Period)) Step(+1);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) WriteLine();
    }

    /// <summary>The prototype's gear (the game's own entry in the main-slot list — the one that gets equipped).</summary>
    private static GearIDRange Prototype()
    {
        try
        {
            foreach (var g in GearManager.GetAllGearForSlot(InventorySlot.GearStandard))
                if (g != null && g.GetCompID(eGearComponent.Category) == _protoCat) return g;
        }
        catch (Exception e) { if (_missLogs++ < 3) ModInfo.Log.LogWarning($"[Garage] gear list: {e.Message}"); }
        return null;
    }

    /// <summary>Puts the (changed) prototype in the main slot and has the lobby redraw it.</summary>
    private static void Equip(GearIDRange gear)
    {
        try { GearManager.RegisterGearInSlotAsEquipped(gear.PlayfabItemInstanceId, InventorySlot.GearStandard); } catch { }
        PlayerBackpackManager.EquipLocalGear(gear);
        foreach (var bar in CellMenu.CM_PlayerLobbyBar.instances)
            if (bar?.m_player != null && bar.m_player.IsLocal) bar.m_updateInventory = true;
    }

    private static void Step(int dir)
    {
        var gear = Prototype();
        if (gear == null)
        {
            _status = Lang.T($"{ProtoName} not found in the main-slot weapons", $"{ProtoName} не найден среди оружия основного слота");
            if (_missLogs++ < 3) ModInfo.Log.LogInfo($"[Garage] no prototype among the main-slot gear (category {_protoCat})");
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
            Equip(gear);
            Rebuild(gear);
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
        if (gear == null) { _status = Lang.T($"{ProtoName} not found", $"{ProtoName} не найден"); return; }
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

    // ---------- the turning model on the right ----------

    private static GameObject _preview;
    private static int _buildId;
    private static Camera _cam;
    private static bool _camLogged;

    /// <summary>Assembles the gun again (third-person model) and stands it in front of the lobby camera.</summary>
    private static void Rebuild(GearIDRange gear)
    {
        ClosePreview();
        int id = ++_buildId;
        var cb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<GearManager.delGearAssembled>(new Action<ItemEquippable>(item =>
        {
            try { if (id == _buildId && item != null) Settle(item.gameObject); }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Garage] preview: {e.Message}"); }
        }));
        var built = GearManager.AssembleGearAsync(gear, ItemMode.ThirdPerson, cb);
        if (built != null) _preview = built.gameObject;
        if (_preview != null) Settle(_preview);
    }

    /// <summary>No physics, no colliders; visible to the lobby camera.</summary>
    private static bool _layerSet;

    private static void Settle(GameObject go)
    {
        _preview = go;
        _layerSet = false;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        go.SetActive(true);
    }

    private static Camera LobbyCamera()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        _cam = null;
        var all = Camera.allCameras;
        if (!_camLogged) { _camLogged = true; ModInfo.Log.LogInfo($"[Garage] cameras: {string.Join(", ", all.Select(c => $"{c.name} (depth {c.depth}, fov {c.fieldOfView:0})"))}"); }
        // The camera that draws the lobby's 3D scene: perspective, the deepest that isn't a UI camera.
        foreach (var c in all.OrderByDescending(c => c.depth))
            if (c != null && !c.orthographic && !c.name.ToLowerInvariant().Contains("ui")) { _cam = c; break; }
        return _cam ??= Camera.main;
    }

    private static float _spin;

    /// <summary>The local player's model in the lobby — the gun goes on its layer, at its depth (the lobby lights it there).</summary>
    private static GameObject LocalModel()
    {
        try
        {
            foreach (var bar in CellMenu.CM_PlayerLobbyBar.instances)
                if (bar?.m_player != null && bar.m_player.IsLocal && bar.m_playerModel != null) return bar.m_playerModel;
        }
        catch { }
        return null;
    }

    private static bool _diagLogged;

    private static void PreviewTick()
    {
        if (_preview == null) return;
        var cam = LobbyCamera();
        if (cam == null) return;
        var model = LocalModel();
        var renderers = _preview.GetComponentsInChildren<Renderer>(true);
        if (!_layerSet && renderers.Length > 0)
        {
            _layerSet = true;
            var mr = model != null ? model.GetComponentInChildren<Renderer>(true) : null;
            int layer = mr != null ? mr.gameObject.layer : (cam.cullingMask & 1) != 0 ? 0 : Enumerable.Range(0, 32).FirstOrDefault(i => (cam.cullingMask & (1 << i)) != 0);
            foreach (var tr in _preview.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
            if (model != null && model.transform.parent != null) _preview.transform.SetParent(model.transform.parent, true); // in the lobby scene, like the players
            _preview.transform.localScale = Vector3.one * 1.6f;
            if (!_diagLogged)
            {
                _diagLogged = true;
                ModInfo.Log.LogInfo($"[Garage] preview: camera '{cam.name}' at {cam.transform.position}, model {(model != null ? $"'{model.name}' at {model.transform.position}, layer {layer} '{LayerMask.LayerToName(layer)}'" : "not found")}, renderers {renderers.Length}");
            }
        }
        foreach (var r in renderers) if (r != null && !r.enabled) r.enabled = true; // the gear keeps its parts hidden until wielded
        float depth = model != null ? Math.Max(0.8f, Vector3.Dot(model.transform.position - cam.transform.position, cam.transform.forward) * 0.75f) : 2f;
        var t = _preview.transform;
        t.position = cam.ViewportToWorldPoint(new Vector3(0.68f, 0.55f, depth));
        _spin = (_spin + Time.unscaledDeltaTime * 30f) % 360f;
        t.rotation = Quaternion.LookRotation(cam.transform.right, cam.transform.up) * Quaternion.Euler(0f, _spin, 0f);
    }

    private static void ClosePreview()
    {
        if (_preview != null) UnityEngine.Object.Destroy(_preview);
        _preview = null;
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
        GUI.Label(new Rect(x + line * 0.5f, y + line * 0.3f, w, line), Lang.T("GARAGE — W S part, A D change, Enter — write, F8 — close", "ГАРАЖ — W S деталь, A D сменить, Enter — записать, F8 — закрыть"), _style);
        float cy = y + line * 1.5f;
        if (gear == null)
        {
            GUI.color = new Color(1f, 0.5f, 0.4f, 1f);
            GUI.Label(new Rect(x + line * 0.5f, cy, w, line), Lang.T($"{ProtoName} not found in the main-slot weapons", $"{ProtoName} не найден среди оружия основного слота"), _style);
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
