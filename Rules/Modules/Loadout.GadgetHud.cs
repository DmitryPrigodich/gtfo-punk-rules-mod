using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Gadget panel above the health bar (screen-space, so the HUD's own scale doesn't matter):
///   - a line "BIO-TRACKER [N]  ON" with the battery as a progress bar and percent, for both gadgets;
///   - the bio-tracker screen above it while the bio-tracker is on.
/// The stock booster icons above health are hidden (boosters are replaced by the loadout).
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<bool> _gadgetHud, _hideBoosterIcons;
    private static ConfigEntry<float> _gadgetHudAlpha, _gadgetBottom, _gadgetScale;

    private void BindGadgetHud()
    {
        _gadgetHud = Bind("GadgetHud", true, "Show the gadget panel above the health bar: on/off, the battery bar, and the bio-tracker screen.");
        _gadgetHudAlpha = Bind("GadgetHudOpacity", 0.9f, "Opacity of the gadget panel (0–1).");
        _gadgetBottom = Bind("GadgetPanelBottom", 0.1f, "Where the gadget panel's bottom edge is: fraction of the screen height from the bottom (the health bar is at ~0.085).");
        _gadgetScale = Bind("GadgetPanelScale", 1f, "Size of the gadget panel (1 — about a third of the screen height wide).");
        _hideBoosterIcons = Bind("HideBoosterIcons", true, "Hide the stock booster icons above health (boosters are replaced by the loadout).");
    }

    private static bool _gadgetHooked, _gadgetShow;
    private static PerkKind _gadgetPerk;
    private static PUI_LocalPlayerStatus _status;
    private static float _nextStatusFind, _nextBoosterHide;

    private static void GadgetHudTick(bool inLevel, PerkKind perk)
    {
        _gadgetPerk = perk;
        _gadgetShow = _gadgetHud.Value && inLevel && perk != PerkKind.None && GameUtil.LocalAgent != null;
        if (!_gadgetShow) return;
        if (!_gadgetHooked) { _gadgetHooked = true; Overlay.Ensure(); Overlay.OnDraw += DrawGadgetPanel; }
        if (Time.time >= _nextBoosterHide) { _nextBoosterHide = Time.time + 0.5f; HideBoosters(); }
    }

    /// <summary>The stock booster icons stay hidden (the game may switch them back on).</summary>
    private static void HideBoosters()
    {
        if (!_hideBoosterIcons.Value) return;
        if (_status == null && Time.time >= _nextStatusFind) { _nextStatusFind = Time.time + 3f; _status = UnityEngine.Object.FindObjectOfType<PUI_LocalPlayerStatus>(); }
        var d = _status?.m_boosterIconActiveDisplay;
        if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
    }

    // ---------- drawing ----------

    private static GUIStyle _gStyle, _gSmall;
    private static Font _gFont;
    private static bool _gFontTried;

    /// <summary>The HUD's own font for the immediate-mode GUI, if the game kept the source font (otherwise the default one).</summary>
    private static Font HudFont()
    {
        if (_gFontTried) return _gFont;
        _gFontTried = true;
        try
        {
            var tmp = _status?.m_healthText ?? UnityEngine.Object.FindObjectOfType<PUI_LocalPlayerStatus>()?.m_healthText;
            _gFont = tmp?.font?.sourceFontFile;
        }
        catch { }
        ModInfo.Log.LogInfo($"[Loadout] gadget panel font: {(_gFont != null ? _gFont.name : "default")}");
        return _gFont;
    }

    private static void DrawGadgetPanel()
    {
        if (!_gadgetShow) return;
        var focus = FocusStateManager.CurrentState;
        if (focus != eFocusState.FPS && focus != eFocusState.FPS_TypingInChat && focus != eFocusState.FPS_CommunicationDialog) return;
        var agent = GameUtil.LocalAgent;
        if (agent == null || !agent.Alive) return;
        _texWhite ??= MakeWhite();

        float H = Screen.height, k = Mathf.Clamp(_gadgetScale.Value, 0.3f, 3f);
        float a = Mathf.Clamp01(_gadgetHudAlpha.Value);
        float lineH = H * 0.026f * k, width = H * 0.36f * k;
        float bottom = H * (1f - Mathf.Clamp(_gadgetBottom.Value, 0f, 0.9f));
        var lineRect = new Rect(Screen.width * 0.5f - width * 0.5f, bottom - lineH, width, lineH);

        if (_gStyle == null)
        {
            var font = HudFont();
            _gStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            _gSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false, clipping = TextClipping.Overflow };
            if (font != null) { _gStyle.font = font; _gSmall.font = font; }
        }
        _gStyle.fontSize = Math.Max(8, (int)(lineH * 0.62f));
        _gSmall.fontSize = Math.Max(7, (int)(lineH * 0.45f));
        var old = GUI.color;

        // The line: name, key, state | battery bar + percent.
        bool on = GadgetOn(_gadgetPerk);
        var pale = new Color(0.82f, 0.9f, 0.95f, a);
        Box(lineRect, new Color(0.02f, 0.05f, 0.08f, 0.55f * a), new Color(pale.r, pale.g, pale.b, 0.45f * a));
        string name = _gadgetPerk switch
        {
            PerkKind.BioTracker => Lang.T("BIO-TRACKER", "БИОТРЕКЕР"),
            PerkKind.Hacker => Lang.T("HACKING TOOL", "ВЗЛОМ"),
            PerkKind.SoftBoots => Lang.T("SOFT BOOTS", "МЯГКИЕ БОТИНКИ"),
            _ => Lang.T("THERMAL", "ТЕПЛОВИЗОР"),
        };
        string state = _gadgetPerk == PerkKind.Hacker ? Lang.T("TAKE OUT", "ДОСТАТЬ") : _gadgetPerk == PerkKind.SoftBoots ? Lang.T("ALWAYS ON", "ВСЕГДА") : on ? Lang.T("ON", "ВКЛ") : Lang.T("OFF", "ВЫКЛ");
        if (on && _gadgetPerk == PerkKind.Thermal && _zoom > 0.01f) state += $" ×{1f / (1f - _zoom):0.#}";
        GUI.color = pale;
        GUI.Label(new Rect(lineRect.x + lineH * 0.3f, lineRect.y, lineRect.width * 0.6f, lineH), _gadgetPerk == PerkKind.SoftBoots ? name : $"{name} [{_gadgetKey.Value.ToUpperInvariant()}]", _gStyle);
        GUI.color = on ? new Color(1f, 0.7f, 0.3f, a) : new Color(pale.r, pale.g, pale.b, 0.6f * a);
        float nameW = _gStyle.CalcSize(new GUIContent(_gadgetPerk == PerkKind.SoftBoots ? name : $"{name} [{_gadgetKey.Value.ToUpperInvariant()}]")).x;
        GUI.Label(new Rect(lineRect.x + lineH * 0.3f + nameW + lineH * 0.4f, lineRect.y, lineH * 4f, lineH), state, _gStyle);

        if (_batterySeconds.Value > 0f && _gadgetPerk != PerkKind.Hacker && _gadgetPerk != PerkKind.SoftBoots)
        {
            // Battery: a framed progress bar on the right, percent next to it.
            float barW = width * 0.3f, barH = lineH * 0.36f;
            var bar = new Rect(lineRect.xMax - barW - lineH * 1.9f, lineRect.center.y - barH * 0.5f, barW, barH);
            var col = _charge < Mathf.Clamp01(_minCharge.Value) ? new Color(0.95f, 0.3f, 0.25f, a) : _charge < 0.35f ? new Color(0.95f, 0.75f, 0.3f, a) : pale;
            Box(bar, new Color(0f, 0f, 0f, 0.4f * a), new Color(col.r, col.g, col.b, 0.7f * a));
            GUI.color = col;
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, Math.Max(0f, (bar.width - 4f) * Mathf.Clamp01(_charge)), bar.height - 4f), _texWhite);
            var pctStyle = _gSmall; pctStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(bar.xMax, lineRect.y, lineH * 1.75f, lineH), $"{Mathf.CeilToInt(_charge * 100f)}%", pctStyle);
            pctStyle.alignment = TextAnchor.UpperLeft;
        }

        // The bio-tracker screen above the line.
        if (_gadgetPerk == PerkKind.BioTracker && _radarOn)
        {
            float ph = width * 0.55f;
            DrawRadar(new Rect(lineRect.x, lineRect.y - ph - lineH * 0.25f, width, ph), a, _gSmall);
        }
        GUI.color = old;
    }
}
