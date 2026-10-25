using Il2CppInterop.Runtime;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "Rules of the party": settings everyone must share (door alarm cap, …), chosen by the host right in the lobby
/// instead of the config. Buttons like the lobby's own, to the right of READY FOR CAGE DROP; the host clicks a rule to step through its
/// values, the others see the host's choice. The host's choice is saved
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
        public Func<int, string> Describe; // optional: what the current value means, shown next to the button
        public int Value;
    }

    private static readonly List<Rule> _rules = new();
    private static ConfigEntry<bool> _panel;

    /// <summary>Adds a rule (call from a module's OnEnable). Its current value is applied right away.</summary>
    public static Rule Register(string key, Func<string> title, Func<int, string> format, int min, int max, ConfigEntry<int> config, Action<int> apply, Func<int, string> describe = null)
    {
        var r = new Rule { Key = key, Title = title, Format = format, Min = min, Max = max, Config = config, Apply = apply, Describe = describe };
        r.Value = Mathf.Clamp(config.Value, min, max);
        _rules.Add(r);
        return r;
    }

    protected override void OnBindConfig()
    {
        _panel = Bind("LobbyPanel", true, "Show the rules panel in the lobby (the host changes the rules there).");
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

    // ---------- lobby buttons: copies of the lobby's own button, to the right of READY FOR CAGE DROP ----------

    private class View { public GameObject Go; public CellMenu.CM_Item Item; public TMPro.TextMeshPro Info; public bool Hover; }

    private static readonly List<View> _views = new();
    private static CellMenu.CM_PageLoadout _viewPage;
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

        if (!Visible() || !EnsureViews()) return;
        for (int i = 0; i < _views.Count && i < _rules.Count; i++)
        {
            var r = _rules[i];
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
        if (index < 0 || index >= _rules.Count) return;
        if (!(SNet.IsMaster || !SNet.HasMaster)) { ModInfo.Log.LogInfo("[PartyRules] only the host changes the rules"); return; }
        var r = _rules[index];
        int next = r.Value + dir;
        if (next > r.Max) next = r.Min;
        if (next < r.Min) next = r.Max;
        r.Config.Value = next;
        Set(r, next, "host clicked");
        if (SNet.IsMaster) Broadcast();
    }

    /// <summary>Copies the lobby's small button (the look, the hover and the click come from the game) once per lobby page.</summary>
    private static bool EnsureViews()
    {
        var page = CellMenu.CM_PageLoadout.Current;
        if (_viewPage == page && _views.Count == _rules.Count && _views.All(v => v.Go != null)) return true;
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
        for (int i = 0; i < _rules.Count; i++)
        {
            var go = UnityEngine.Object.Instantiate(src.gameObject, parent, false);
            go.name = "GtfoPunk_PartyRule_" + _rules[i].Key;
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

            // What the value means: a smaller text to the right of the button.
            if (_rules[i].Describe != null && item.m_texts != null && item.m_texts.Length > 0 && item.m_texts[0] != null)
            {
                var src0 = item.m_texts[0];
                var tp = src0.transform.parent; // same space as the button's own text (same scale)
                var infoGo = UnityEngine.Object.Instantiate(src0.gameObject, tp, false);
                infoGo.name = go.name + "_Info";
                var info = infoGo.GetComponent<TMPro.TextMeshPro>();
                info.enableAutoSizing = false;
                info.enableWordWrapping = false;
                info.fontSize = src0.fontSize * 0.6f;
                info.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                info.rectTransform.pivot = new Vector2(0f, 0.5f);
                info.SetText(_rules[i].Describe(_rules[i].Value));
                var b2 = LocalBox(go, tp);
                var bb = LocalBox(go, parent);
                infoGo.transform.localPosition = new Vector3(b2.max.x + (b2.size.y * 0.3f), b2.center.y, infoGo.transform.localPosition.z);
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
