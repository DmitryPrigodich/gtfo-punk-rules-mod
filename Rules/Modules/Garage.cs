using GameData;
using Il2CppInterop.Runtime;
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
    private static ConfigEntry<string> _pvPos;
    private static ConfigEntry<float> _pvDepth, _pvSize, _pvSpin;
    private static ConfigEntry<bool> _hideUi;

    private const string ProtoName = "DRED GARAGE PROTOTYPE";
    private const string ProtoBody = "Van Auken LTC5";

    protected override void OnBindConfig()
    {
        _key = Bind("Key", KeyCode.F8, "Opens / closes the garage panel in the lobby.");
        _proto = Bind("Prototype", ProtoBody + " + 12=15, 19=3, 21=10, 23=1 | type=Prototype",
            "The prototype's parts (written by the garage): 'Body + part=ID, … | options' — as in SilencedGun.MainGuns. Applied after a game restart; in the lobby the garage changes the gun at once.");
        _hideUi = Bind("HideLobbyUi", true, "While the garage is open, the players' loadout cards in the lobby are hidden (the gun turns in front of them).");
        _pvPos = Bind("PreviewPosition", "0.62,0.5", "Where the turning gun stands on the screen: 'x,y' from 0 (left / bottom) to 1 (right / top).");
        _pvDepth = Bind("PreviewDepth", 0.8f, "How far the gun stands, as a part of the distance to the players (1 — next to them, 0.5 — halfway to the camera).");
        _pvSize = Bind("PreviewSize", 1.2f, "Gun size against the players' scale (the lobby scene is scaled up, so the gun takes the players' scale times this).");
        _pvSpin = Bind("PreviewSpin", 30f, "Turning speed, degrees per second (0 — still, side view).");
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
        if (!lobby) { if (_open) ClosePreview(); _open = false; LobbyUi(true); return; }
        if (Input.GetKeyDown(_key.Value))
        {
            _open = !_open;
            if (_open)
            {
                _cam = null; _layer = -1; _diagLogged = false; // the lobby may be a new scene since last time
                BuildRows();
                var g = Prototype();
                if (g != null) { try { Equip(g); Rebuild(g); _status = ""; } catch (Exception e) { _status = e.Message; } }
                if (!_hooked) { _hooked = true; Overlay.Ensure(); Overlay.OnDraw += Draw; }
            }
        }
        if (!_open) { ClosePreview(); LobbyUi(true); return; }
        if (_hideUi.Value) LobbyUi(false);
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
    private static int _layer = -1;
    private static float _modelScale = 1f;
    private static bool _diagLogged;

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

    /// <summary>No physics, no colliders.</summary>
    private static void Settle(GameObject go)
    {
        _preview = go;
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        go.SetActive(true);
    }

    /// <summary>The local player's model in the lobby — the gun goes on its layer, in front of it.</summary>
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

    /// <summary>The layer the model is actually drawn on: the most common one among its mesh renderers.</summary>
    private static int ModelLayer(GameObject model)
    {
        var counts = new Dictionary<int, int>();
        Bounds? box = null;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r.TryCast<SkinnedMeshRenderer>() == null && r.TryCast<MeshRenderer>() == null) continue;
            int l = r.gameObject.layer;
            counts[l] = counts.TryGetValue(l, out var n) ? n + 1 : 1;
            if (box == null) box = r.bounds; else { var b = box.Value; b.Encapsulate(r.bounds); box = b; }
        }
        // A person is ~1.8 m tall: the scene's scale from the model's real height on screen (its transform may say 1).
        _modelScale = box != null && box.Value.size.y > 0.01f ? box.Value.size.y / 1.8f : model.transform.lossyScale.y;
        ModInfo.Log.LogInfo($"[Garage] player height {(box != null ? box.Value.size.y : 0f):0.##} units, transform scale {model.transform.lossyScale.y:0.##} → scene scale {_modelScale:0.##}");
        ModInfo.Log.LogInfo($"[Garage] model layers: {string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} '{LayerMask.LayerToName(kv.Key)}' ×{kv.Value}"))}");
        return counts.Count > 0 ? counts.OrderByDescending(kv => kv.Value).First().Key : model.layer;
    }

    /// <summary>
    /// The camera that draws the lobby players: any camera (also a disabled one the game renders by hand) whose
    /// culling mask has the players' layer and whose view has the model in it. All cameras are logged once.
    /// </summary>
    private static Camera FindCamera(GameObject model, int layer)
    {
        Camera best = null;
        int bestScore = int.MinValue;
        var lines = new List<string>();
        foreach (var o in UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>()))
        {
            var c = o.TryCast<Camera>();
            if (c == null || c.gameObject.scene.name == null) continue; // prefabs/assets have no scene
            var vp = c.WorldToViewportPoint(model.transform.position);
            bool sees = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.2f && vp.y < 1.2f;
            bool mask = (c.cullingMask & (1 << layer)) != 0;
            lines.Add($"{c.name} [{(c.enabled && c.gameObject.activeInHierarchy ? "on" : "off")}, depth {c.depth}, fov {c.fieldOfView:0}, mask {c.cullingMask:X8}, rt {(c.targetTexture != null ? c.targetTexture.name : "-")}, at {c.transform.position}, model at {vp.x:0.00}/{vp.y:0.00}/{vp.z:0.0}]");
            if (c.orthographic || !sees) continue;
            int score = (mask ? 100 : 0) + (c.enabled ? 10 : 0) + (c.targetTexture == null ? 5 : 0);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        ModInfo.Log.LogInfo($"[Garage] cameras: {string.Join("; ", lines)} → {(best != null ? best.name : "none")}");
        return best;
    }

    private static float _spin;

    private static void PreviewTick()
    {
        if (_preview == null) return;
        var model = LocalModel();
        if (model == null) return;
        if (_layer < 0) _layer = ModelLayer(model);
        if (_cam == null) { _cam = FindCamera(model, _layer); if (_cam == null) return; }
        var cam = _cam;

        int shown = 0, all = 0;
        foreach (var tr in _preview.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = _layer; // also parts that load later
        foreach (var r in _preview.GetComponentsInChildren<Renderer>(true))
        {
            all++;
            if (r.enabled) shown++;
        }
        var t = _preview.transform;
        if (t.parent != null) t.SetParent(null, true);
        // The lobby scene is scaled up (players ~1000×, thousands of units from the camera): the gun takes their scale.
        float modelDepth = Vector3.Dot(model.transform.position - cam.transform.position, cam.transform.forward);
        float depth = Math.Max(cam.nearClipPlane * 2f, modelDepth * Mathf.Clamp(_pvDepth.Value, 0.05f, 1.5f));
        var (vx, vy) = ParseXY(_pvPos.Value, 0.62f, 0.5f);
        t.position = cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
        t.localScale = Vector3.one * (_modelScale * _pvSize.Value);
        _spin = (_spin + Time.unscaledDeltaTime * _pvSpin.Value) % 360f;
        t.rotation = Quaternion.LookRotation(cam.transform.right, cam.transform.up) * Quaternion.Euler(0f, _spin, 0f);
        if (!_diagLogged && all > 0)
        {
            _diagLogged = true;
            ModInfo.Log.LogInfo($"[Garage] preview: camera '{cam.name}', model '{model.name}' at {model.transform.position}, layer {_layer} '{LayerMask.LayerToName(_layer)}', scene scale {_modelScale:0.##}, players at depth {modelDepth:0}, gun at {t.position} (depth {depth:0}, scale {t.localScale.y:0.##}), renderers {shown}/{all} on");
        }
    }

    private static bool _uiHidden;

    /// <summary>
    /// The game's own "hide the loadout" (used for the clothes view) on every lobby bar. Hidden once on opening; while
    /// the garage is open the game's own calls to show the cards again (bots' bars refresh now and then) are turned
    /// into "hide" by a patch — re-hiding them from here made them blink.
    /// </summary>
    private static void LobbyUi(bool show)
    {
        if (show == !_uiHidden) return;
        _uiHidden = !show;
        try
        {
            foreach (var bar in CellMenu.CM_PlayerLobbyBar.instances)
                if (bar != null) bar.HideLoadoutUI(!show, true);
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Garage] lobby UI: {e.Message}"); }
    }

    [HarmonyPatch(typeof(CellMenu.CM_PlayerLobbyBar), nameof(CellMenu.CM_PlayerLobbyBar.HideLoadoutUI))]
    private static class Patch_HideLoadout
    {
        private static void Prefix(ref bool hide)
        {
            if (_uiHidden) hide = true;
        }
    }

    private static (float, float) ParseXY(string v, float dx, float dy)
    {
        var p = (v ?? "").Split(',');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float x = p.Length > 0 && float.TryParse(p[0].Trim(), System.Globalization.NumberStyles.Float, ci, out var a) ? a : dx;
        float y = p.Length > 1 && float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, ci, out var b) ? b : dy;
        return (x, y);
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
