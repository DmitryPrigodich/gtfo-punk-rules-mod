using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "Rules of the party": settings everyone must share (door alarm cap, …), chosen by the host right in the lobby
/// instead of the config. A small panel at the top of the lobby screen; the host clicks a rule to step through its
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
        _pos = Bind("PanelPosition", "0.5,0.11", "Where the panel sits on the lobby screen (x,y as fractions of the screen; 0,0 — top left).");
        _size = Bind("PanelTextSize", 0.022f, "Panel text size, as a fraction of the screen height.");
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

    // ---------- lobby panel ----------

    private static bool _hooked;
    private static readonly List<Rect> _rects = new();

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

        if (!Visible()) return;
        if (!_hooked) { _hooked = true; Overlay.Ensure(); Overlay.OnDraw += Draw; }
        if (!SNet.IsMaster && SNet.HasMaster) return; // only the host clicks
        bool left = Input.GetMouseButtonDown(0), right = Input.GetMouseButtonDown(1);
        if (!left && !right) return;
        var m = Input.mousePosition;
        var p = new Vector2(m.x, Screen.height - m.y); // GUI space: y from the top
        for (int i = 0; i < _rects.Count && i < _rules.Count; i++)
        {
            if (!_rects[i].Contains(p)) continue;
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

    private static GUIStyle _style, _titleStyle;
    private static Texture2D _bg;

    private static void Draw()
    {
        if (!Visible()) return;
        float h = Screen.height * Mathf.Clamp(_size.Value, 0.01f, 0.06f);
        if (_bg == null)
        {
            _bg = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _bg.SetPixel(0, 0, Color.white);
            _bg.Apply();
        }
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        _titleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        _style.fontSize = (int)h;
        _titleStyle.fontSize = (int)(h * 0.7f);

        var pos = TurretVariants.Vec(_pos.Value);
        float cx = Screen.width * pos.x, y = Screen.height * pos.y;
        bool host = SNet.IsMaster || !SNet.HasMaster;
        var old = GUI.color;

        GUI.color = new Color(0.6f, 0.8f, 0.75f, 0.8f);
        string head = Lang.T("PARTY RULES", "ПРАВИЛА ПАРТИИ") + (host ? Lang.T("  (click to change)", "  (клик — изменить)") : Lang.T("  (set by the host)", "  (выбирает хост)"));
        GUI.Label(new Rect(cx - h * 15f, y, h * 30f, h * 1.2f), head, _titleStyle);
        y += h * 1.3f;

        var m = Input.mousePosition;
        var mouse = new Vector2(m.x, Screen.height - m.y);
        _rects.Clear();
        foreach (var r in _rules)
        {
            string text = $"{r.Title()}: {r.Format(r.Value)}";
            float w = Math.Max(h * 12f, _style.CalcSize(new GUIContent(text)).x + h * 2f);
            var rect = new Rect(cx - w * 0.5f, y, w, h * 1.6f);
            _rects.Add(rect);
            bool hover = host && rect.Contains(mouse);
            GUI.color = new Color(0.02f, 0.1f, 0.09f, hover ? 0.85f : 0.6f);
            GUI.DrawTexture(rect, _bg);
            GUI.color = hover ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(0.55f, 1f, 0.85f, 0.95f);
            GUI.Label(rect, text, _style);
            y += h * 1.8f;
        }
        GUI.color = old;
    }
}
