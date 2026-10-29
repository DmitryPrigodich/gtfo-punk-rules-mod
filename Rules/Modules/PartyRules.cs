using Il2CppInterop.Runtime;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "Rules of the party": settings everyone must share (difficulty, alarm settings), chosen by the host right in the lobby
/// instead of the config. Buttons like the lobby's own, to the right of READY FOR CAGE DROP; the host clicks a rule to step through its
/// values, the others see the host's choice. The host's choice is saved
/// to the host's config; the others get it over the network and their own config is left alone.
/// Other modules add their rules with <see cref="Register"/>.
/// </summary>
public class PartyRules : ModuleBase
{
    public override string Id => "PartyRules";
    public override string Description => "Shared game rules (difficulty, alarm settings) picked by the host in the lobby and sent to everyone.";
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
        public Func<int, string> Describe; // optional: what the current value means, shown next to the button
        public bool Hidden;                 // synced like the others, but no lobby button (set in the config)
        public Func<int> HostValue;         // optional: the host's value comes from here instead of Config (a derived value)
        public int Value;
    }

    private static readonly List<Rule> _rules = new();
    /// <summary>The rules that have a lobby button (in order).</summary>
    private static List<Rule> Shown => _rules.Where(r => !r.Hidden).ToList();
    private static ConfigEntry<bool> _panel;
    private static ConfigEntry<float> _infoSize;

    /// <summary>Adds a rule (call from a module's OnEnable). Its current value is applied right away.</summary>
    public static Rule Register(string key, Func<string> title, Func<int, string> format, int min, int max, ConfigEntry<int> config, Action<int> apply,
        Func<int, string> describe = null, bool hidden = false, Func<int> hostValue = null)
    {
        var r = new Rule { Key = key, Title = title, Format = format, Min = min, Max = max, Config = config, Apply = apply, Describe = describe, Hidden = hidden, HostValue = hostValue };
        r.Value = Mathf.Clamp(hostValue != null ? hostValue() : config.Value, min, max);
        _rules.Add(r);
        return r;
    }

    protected override void OnBindConfig()
    {
        _panel = Bind("LobbyPanel", true, "Show the rules panel in the lobby (the host changes the rules there).");
        _infoSize = Bind("DescriptionSize", 0.42f, "Size of the text next to a rule button (what the value means), relative to the button's text.");
        ConfigMigration.DropOrphans(Config, Section); // PanelPosition (0.9.2) had a broken default
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

    // ---------- lobby buttons: copies of the lobby's own button, to the right of READY FOR CAGE DROP ----------

    private class View { public GameObject Go; public CellMenu.CM_Item Item; public TMPro.TextMeshPro Info; public bool Hover; }

    private static readonly List<View> _views = new();
    private static CellMenu.CM_PageLoadout _viewPage;
    private static float _nextViewTry;

    private static bool Visible()
    {
        if (!_panel.Value || Shown.Count == 0 || GameStateManager.CurrentStateName != eGameStateName.Lobby) return false;
        var page = CellMenu.CM_PageLoadout.Current;
        return page != null && page.m_isActive;
    }

    private static void Tick()
    {
        var state = GameStateManager.CurrentStateName;
        bool preGame = state == eGameStateName.Lobby || state == eGameStateName.Generating || state == eGameStateName.ReadyToStopElevatorRide;
        // The host owns the rules: its config values, broadcast while in the lobby.
        if (SNet.IsMaster || !SNet.HasMaster)
            foreach (var r in _rules) Set(r, r.HostValue != null ? r.HostValue() : r.Config.Value, "config");
        if (SNet.IsMaster && preGame && Time.time >= _nextBroadcast && SNet.HasMaster) Broadcast();

        if (!Visible() || !EnsureViews()) return;
        var shown = Shown;
        for (int i = 0; i < _views.Count && i < shown.Count; i++)
        {
            var r = shown[i];
            string text = $"{r.Title()}\n{r.Format(r.Value)}".ToUpperInvariant();
            var tmp = _views[i].Item.m_texts != null && _views[i].Item.m_texts.Length > 0 ? _views[i].Item.m_texts[0] : null;
            if (tmp != null && tmp.text != text) tmp.SetText(text);
            var info = _views[i].Info;
            if (info != null && r.Describe != null)
            {
                string d = r.Describe(r.Value);
                if (info.text != d) info.SetText(d);
            }
            // Right click on a button — one step back.
            if (_views[i].Hover && Input.GetMouseButtonDown(1)) Step(i, -1);
        }
    }

    private static void Clicked(int index) => Step(index, +1);

    private static void Step(int index, int dir)
    {
        var shown = Shown;
        if (index < 0 || index >= shown.Count) return;
        if (!(SNet.IsMaster || !SNet.HasMaster)) { ModInfo.Log.LogInfo("[PartyRules] only the host changes the rules"); return; }
        var r = shown[index];
        int next = r.Value + dir;
        if (next > r.Max) next = r.Min;
        if (next < r.Min) next = r.Max;
        if (r.Config != null) r.Config.Value = next;
        Set(r, next, "host clicked");
        if (SNet.IsMaster) Broadcast();
    }

    /// <summary>Copies the lobby's small button (the look, the hover and the click come from the game) once per lobby page.</summary>
    private static bool EnsureViews()
    {
        var page = CellMenu.CM_PageLoadout.Current;
        var shown = Shown;
        if (_viewPage == page && _views.Count == shown.Count && _views.All(v => v.Go != null)) return true;
        if (Time.time < _nextViewTry) return false;
        _nextViewTry = Time.time + 2f;
        foreach (var v in _views) { if (v.Go != null) UnityEngine.Object.Destroy(v.Go); if (v.Info != null) UnityEngine.Object.Destroy(v.Info.gameObject); }
        _views.Clear();

        var src = page.m_changeLoadoutButton ?? page.m_copyLobbyIdButton;
        var ready = page.m_readyButton;
        if (src == null || ready == null) { ModInfo.Log.LogWarning("[PartyRules] lobby buttons not found"); return false; }
        var parent = src.transform.parent;
        var readyBox = LocalBox(ready.gameObject, parent);
        if (readyBox.size == Vector3.zero) return false;

        float x = readyBox.max.x;
        for (int i = 0; i < shown.Count; i++)
        {
            var go = UnityEngine.Object.Instantiate(src.gameObject, parent, false);
            go.name = "GtfoPunk_PartyRule_" + shown[i].Key;
            go.SetActive(true);
            var item = go.GetComponent<CellMenu.CM_Item>();
            if (item == null) { UnityEngine.Object.Destroy(go); return false; }
            try { item.SetupCMItem(); } catch { }
            int index = i;
            item.SetOnBtnPressCallback(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int>>(new Action<int>(_ => Clicked(index))));
            if (item.m_texts != null)
                foreach (var t in item.m_texts) if (t != null) { t.enableWordWrapping = false; t.enableAutoSizing = true; t.fontSizeMin = t.fontSize * 0.4f; t.fontSizeMax = t.fontSize; }

            // To the right of the ready button, vertically centred on it.
            var box = LocalBox(go, parent);
            float gap = readyBox.size.y * 0.25f;
            var shift = new Vector3(x + gap - box.min.x, readyBox.center.y - box.center.y, 0f);
            go.transform.localPosition += shift;
            x += gap + box.size.x;
            var view = new View { Go = go, Item = item };
            try
            {
                item.OnBtnHoverChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<int, bool>>(new Action<int, bool>((_, on) => view.Hover = on));
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[PartyRules] hover: {e.Message}"); }

            // What the value means: a small plain text to the right of the button (not part of the button).
            if (shown[i].Describe != null && item.m_texts != null && item.m_texts.Length > 0 && item.m_texts[0] != null)
            {
                var src0 = item.m_texts[0];
                var infoGo = UnityEngine.Object.Instantiate(src0.gameObject, src0.transform.parent, false);
                infoGo.name = go.name + "_Info";
                infoGo.transform.SetParent(parent, true); // out of the button: no hover / click look
                for (int c = infoGo.transform.childCount - 1; c >= 0; c--) UnityEngine.Object.Destroy(infoGo.transform.GetChild(c).gameObject);
                var info = infoGo.GetComponent<TMPro.TextMeshPro>();
                info.enableAutoSizing = false;
                info.enableWordWrapping = false;
                info.fontSize = src0.fontSize * Mathf.Clamp(_infoSize.Value, 0.2f, 1f);
                info.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                info.rectTransform.pivot = new Vector2(0f, 0.5f);
                info.color = new Color(info.color.r, info.color.g, info.color.b, 0.75f);
                info.SetText(shown[i].Describe(shown[i].Value));
                var bb = LocalBox(go, parent);
                infoGo.transform.localPosition = new Vector3(bb.max.x + bb.size.y * 0.25f, bb.center.y, infoGo.transform.localPosition.z);
                view.Info = info;
                x += gap + bb.size.x * 1.3f; // room for the text before the next button
            }
            _views.Add(view);
        }
        _viewPage = page;
        ModInfo.Log.LogInfo($"[PartyRules] lobby buttons made: {_views.Count} (copy of {src.GetIl2CppType().Name} '{src.name}'), next to '{ready.name}'");
        return true;
    }

    /// <summary>Bounds of an object's visible parts in the parent's local space (x, y; z ignored).</summary>
    private static Bounds LocalBox(GameObject go, Transform parent)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled) continue;
            foreach (var corner in new[] { r.bounds.min, r.bounds.max })
            {
                var p = parent.InverseTransformPoint(corner);
                p.z = 0f;
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }
}
