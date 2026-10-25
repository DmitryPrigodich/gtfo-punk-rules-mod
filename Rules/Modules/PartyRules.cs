using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "Rules of the party": settings everyone must share (door alarm cap, …), chosen by the host right in the lobby
/// instead of the config. Framed lines in the lobby's own font at the top of the lobby screen; the host clicks a rule to step through its
/// values (left click — next, right click — previous), the others see the host's choice. The host's choice is saved
/// to the host's config; the others get it over the network and their own config is left alone.
/// Other modules add their rules with <see cref="Register"/>.
/// </summary>
public class PartyRules : ModuleBase
{
    public override string Id => "PartyRules";
    public override string Description => "Shared game rules (door alarm cap, …) picked by the host in the lobby and sent to everyone.";
    public override ModuleSide Side => ModuleSide.Everyone;

    /// <summary>One rule: an integer setting from Min to Max, backed by a config entry (used by the host and when playing alone).</summary>
    public sealed class Rule
    {
        public string Key;
        public Func<string> Title;
        public Func<int, string> Format;
        public int Min, Max;
        public ConfigEntry<int> Config;
        public Action<int> Apply;
        public int Value;
    }

    private static readonly List<Rule> _rules = new();
    private static ConfigEntry<bool> _panel;
    private static ConfigEntry<string> _pos;
    private static ConfigEntry<float> _size;

    /// <summary>Adds a rule (call from a module's OnEnable). Its current value is applied right away.</summary>
    public static Rule Register(string key, Func<string> title, Func<int, string> format, int min, int max, ConfigEntry<int> config, Action<int> apply)
    {
        var r = new Rule { Key = key, Title = title, Format = format, Min = min, Max = max, Config = config, Apply = apply };
        r.Value = Mathf.Clamp(config.Value, min, max);
        _rules.Add(r);
        return r;
    }

    protected override void OnBindConfig()
    {
        _panel = Bind("LobbyPanel", true, "Show the rules panel in the lobby (the host changes the rules there).");
        _pos = Bind("PanelPlace", "0.5,0.12", "Where the panel sits on the lobby screen (x,y as fractions of the screen; 0,0 — top left).");
        _size = Bind("PanelTextSize", 0.022f, "Panel text size (0.022 — like the lobby headers).");
        ConfigMigration.DropOrphans(Config, Id); // PanelPosition (0.9.2) had a broken default
    }

    protected override void OnEnable()
    {
        Net.On(Net.PartyRules, OnReceived);
        FrameTicker.OnUpdate += Tick;
    }

    // ---------- values and sync ----------

    private static void Set(Rule r, int v, string why)
    {
        v = Mathf.Clamp(v, r.Min, r.Max);
        if (v == r.Value) return;
        r.Value = v;
        ModInfo.Log.LogInfo($"[PartyRules] {r.Key} = {v} ({why})");
        try { r.Apply(v); } catch (Exception e) { ModInfo.Log.LogWarning($"[PartyRules] {r.Key}: {e.Message}"); }
    }

    private static float _nextBroadcast;

    private static void Broadcast()
    {
        _nextBroadcast = Time.time + 2f;
        var bytes = new List<byte> { (byte)_rules.Count };
        foreach (var r in _rules)
        {
            var key = System.Text.Encoding.UTF8.GetBytes(r.Key);
            bytes.Add((byte)key.Length);
            bytes.AddRange(key);
            bytes.AddRange(BitConverter.GetBytes((short)r.Value));
        }
        Net.SendToAll(Net.PartyRules, bytes.ToArray());
    }

    private static void OnReceived(ulong sender, byte[] b)
    {
        if (SNet.IsMaster || b == null || b.Length < 1) return;
        if (SNet.Master != null && SNet.Master.Lookup != sender) return; // only the host decides
        int n = b[0], i = 1;
        for (int k = 0; k < n && i < b.Length; k++)
        {
            int len = b[i++];
            if (i + len + 2 > b.Length) break;
            string key = System.Text.Encoding.UTF8.GetString(b, i, len);
            i += len;
            int v = BitConverter.ToInt16(b, i);
            i += 2;
            var r = _rules.FirstOrDefault(x => x.Key == key);
            if (r != null) Set(r, v, "from the host");
        }
    }

    // ---------- lobby panel: framed text in the game's own font, on the lobby page ----------

    private class View
    {
        public GameObject Root;
        public TMPro.TextMeshPro Text;
        public SpriteRenderer Frame, Fill;
        public Rect Screen;
    }

    private static readonly List<View> _views = new();
    private static CellMenu.CM_PageLoadout _viewPage;
    private static Camera _uiCam;
    private static Sprite _frameSprite, _fillSprite;
    private static float _nextViewTry;

    private static bool Visible()
    {
        if (!_panel.Value || _rules.Count == 0 || GameStateManager.CurrentStateName != eGameStateName.Lobby) return false;
        var page = CellMenu.CM_PageLoadout.Current;
        return page != null && page.m_isActive;
    }

    private static void Tick()
    {
        var state = GameStateManager.CurrentStateName;
        bool preGame = state == eGameStateName.Lobby || state == eGameStateName.Generating || state == eGameStateName.ReadyToStopElevatorRide;
        // The host owns the rules: its config values, broadcast while in the lobby.
        if (SNet.IsMaster || !SNet.HasMaster)
            foreach (var r in _rules) Set(r, r.Config.Value, "config");
        if (SNet.IsMaster && preGame && Time.time >= _nextBroadcast && SNet.HasMaster) Broadcast();

        bool visible = Visible();
        if (!visible) { foreach (var v in _views) if (v.Root != null && v.Root.activeSelf) v.Root.SetActive(false); return; }
        if (!EnsureViews()) return;
        bool host = SNet.IsMaster || !SNet.HasMaster;
        var m = Input.mousePosition;
        Layout(host, new Vector2(m.x, m.y));

        if (!host) return; // only the host clicks
        bool left = Input.GetMouseButtonDown(0), right = Input.GetMouseButtonDown(1);
        if (!left && !right) return;
        for (int i = 0; i < _views.Count && i < _rules.Count; i++)
        {
            if (!_views[i].Screen.Contains(new Vector2(m.x, m.y))) continue;
            var r = _rules[i];
            int next = r.Value + (left ? 1 : -1);
            if (next > r.Max) next = r.Min;
            if (next < r.Min) next = r.Max;
            r.Config.Value = next;
            Set(r, next, "host clicked");
            try { CellSound.Post(AK.EVENTS.MENU_INJECT_PRESS_AND_HOLD_CANCEL, Vector3.zero); } catch { }
            if (SNet.IsMaster) Broadcast();
        }
    }

    /// <summary>One framed line per rule, made from a copy of the lobby's own header text (same font and look).</summary>
    private static bool EnsureViews()
    {
        var page = CellMenu.CM_PageLoadout.Current;
        if (_viewPage == page && _views.Count == _rules.Count && _views.All(v => v.Root != null) && _uiCam != null) return true;
        if (Time.time < _nextViewTry) return false;
        _nextViewTry = Time.time + 2f;
        foreach (var v in _views) if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
        _views.Clear();

        TMPro.TextMeshPro src = null;
        var bars = CellMenu.CM_PlayerLobbyBar.instances;
        if (bars != null) foreach (var bar in bars) if (bar != null && bar.m_boosterImplantsHeaderText != null) { src = bar.m_boosterImplantsHeaderText; break; }
        if (src == null) { ModInfo.Log.LogWarning("[PartyRules] no lobby text to copy the look from"); return false; }
        int layer = src.gameObject.layer;
        _uiCam = null;
        foreach (var c in Camera.allCameras) if (c != null && (c.cullingMask & (1 << layer)) != 0) { _uiCam = c; break; }
        if (_uiCam == null) { ModInfo.Log.LogWarning("[PartyRules] no camera draws the lobby"); return false; }
        MakeSprites();

        var srcRenderer = src.GetComponent<MeshRenderer>();
        foreach (var r in _rules)
        {
            var root = new GameObject("GtfoPunk_PartyRule");
            root.layer = layer;
            root.transform.SetParent(page.transform, false);
            var textGo = UnityEngine.Object.Instantiate(src.gameObject, root.transform, false);
            textGo.name = "Text";
            textGo.transform.localPosition = Vector3.zero;
            var tmp = textGo.GetComponent<TMPro.TextMeshPro>();
            tmp.enableWordWrapping = false;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.fontSize = src.fontSize * Math.Max(0.3f, _size.Value / 0.022f);
            var v = new View { Root = root, Text = tmp };
            v.Fill = Box(textGo.transform, "Fill", _fillSprite, srcRenderer, -2);
            v.Frame = Box(textGo.transform, "Frame", _frameSprite, srcRenderer, -1);
            _views.Add(v);
        }
        _viewPage = page;
        ModInfo.Log.LogInfo($"[PartyRules] lobby panel made: {_views.Count} rules, font of '{src.name}', camera '{_uiCam.name}'");
        return true;
    }

    private static SpriteRenderer Box(Transform parent, string name, Sprite sprite, MeshRenderer like, int orderOffset)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        if (like != null) { sr.sortingLayerID = like.sortingLayerID; sr.sortingOrder = like.sortingOrder + orderOffset; }
        return sr;
    }

    private static void MakeSprites()
    {
        if (_frameSprite != null && _fillSprite != null) return;
        const int N = 16;
        var frame = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var fill = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool edge = x < 1 || y < 1 || x >= N - 1 || y >= N - 1;
                frame.SetPixel(x, y, edge ? Color.white : Color.clear);
                fill.SetPixel(x, y, Color.white);
            }
        frame.Apply(); fill.Apply();
        var border = new Vector4(4, 4, 4, 4);
        _frameSprite = Sprite.Create(frame, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect, border);
        _fillSprite = Sprite.Create(fill, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect, border);
        _frameSprite.hideFlags = _fillSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    /// <summary>Places the lines under each other at PanelPosition (screen fractions), sizes the frames, remembers their screen rects for clicks.</summary>
    private static void Layout(bool host, Vector2 mouse)
    {
        var pos = TurretVariants.Vec(_pos.Value);
        float sx = Screen.width * pos.x, sy = Screen.height * (1f - pos.y);
        float depth = _uiCam.WorldToScreenPoint(_viewPage.transform.position).z;
        for (int i = 0; i < _views.Count && i < _rules.Count; i++)
        {
            var v = _views[i];
            var r = _rules[i];
            if (!v.Root.activeSelf) v.Root.SetActive(true);
            string text = $"{r.Title()}: {r.Format(r.Value)}".ToUpperInvariant();
            if (v.Text.text != text) v.Text.SetText(text);
            v.Text.ForceMeshUpdate();
            var b = v.Text.textBounds;
            float pad = v.Text.fontSize * 0.45f;
            float line = Math.Max(b.size.y, v.Text.fontSize) + pad * 2f;
            var size = new Vector2(b.size.x + pad * 3f, line);
            // Frame line ~1/12 of the text size thick: the sprite's 1 px border is scaled by its pixels-per-unit.
            float px = Math.Max(0.5f, v.Text.fontSize / 12f);
            foreach (var sr in new[] { v.Fill, v.Frame })
            {
                sr.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
                sr.transform.localScale = new Vector3(px, px, 1f);
                sr.size = size / px;
            }

            var world = _uiCam.ScreenToWorldPoint(new Vector3(sx, sy, depth));
            v.Root.transform.position = world;
            // Screen rect of the frame (for hover and clicks), then step down by its height.
            var bb = v.Frame.bounds;
            var a = _uiCam.WorldToScreenPoint(bb.min);
            var c = _uiCam.WorldToScreenPoint(bb.max);
            v.Screen = Rect.MinMaxRect(Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y));
            sy -= v.Screen.height * 1.25f;

            bool hover = host && v.Screen.Contains(mouse);
            var col = hover ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(0.85f, 0.9f, 0.95f, host ? 1f : 0.7f);
            v.Text.color = col;
            v.Frame.color = new Color(col.r, col.g, col.b, hover ? 0.9f : 0.5f);
            v.Fill.color = new Color(0f, 0f, 0f, hover ? 0.6f : 0.4f);
        }
    }
}
