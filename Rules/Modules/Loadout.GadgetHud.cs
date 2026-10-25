using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The gadget line above the health readout, in the HUD's own font: "BIO-TRACKER [N] on ||||||···· 63%".
/// Sized to a fixed share of the screen height (the HUD's own scale doesn't matter). The stock booster icons
/// that used to sit there are hidden; the bio-tracker screen stands on this line.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<bool> _gadgetHud, _hideBoosterIcons;
    private static ConfigEntry<float> _gadgetHudAlpha, _gadgetHudHeight;
    private static ConfigEntry<string> _gadgetHudOffset;

    private void BindGadgetHud()
    {
        _gadgetHud = Bind("GadgetHud", true, "Show the gadget, on/off and its battery above the health readout.");
        _gadgetHudAlpha = Bind("GadgetHudOpacity", 0.85f, "Opacity of the gadget line (0–1).");
        _gadgetHudHeight = Bind("GadgetHudHeight", 0.02f, "Height of the gadget line text, as a fraction of the screen height.");
        _gadgetHudOffset = Bind("GadgetHudShift", "0,0", "Shift of the gadget line (x right, y up; fractions of the screen height).");
        _hideBoosterIcons = Bind("HideBoosterIcons", true, "Hide the stock booster icons above health (boosters are replaced by the loadout).");
    }

    private static TMPro.TextMeshPro _gadgetText;
    private static PUI_LocalPlayerStatus _status;
    private static float _nextGadgetFind, _nextGadgetFit, _nextBoosterHide;

    private static void GadgetHudTick(bool inLevel, PerkKind perk)
    {
        bool show = _gadgetHud.Value && inLevel && perk != PerkKind.None && GameUtil.LocalAgent != null;
        if (!show) { if (_gadgetText != null) _gadgetText.gameObject.SetActive(false); return; }
        if (_gadgetText == null && !MakeGadgetText()) return;
        if (!_gadgetText.gameObject.activeSelf) _gadgetText.gameObject.SetActive(true);

        string key = _gadgetKey.Value.ToUpperInvariant();
        bool on = GadgetOn(perk);
        string name = perk == PerkKind.BioTracker ? Lang.T("BIO-TRACKER", "БИОТРЕКЕР") : Lang.T("THERMAL", "ТЕПЛОВИЗОР");
        string state = on ? $"<color=#ffb347>{Lang.T("ON", "ВКЛ")}</color>" : Lang.T("OFF", "ВЫКЛ");
        if (on && perk == PerkKind.Thermal && _zoom > 0.01f) state += $" ×{1f / (1f - _zoom):0.#}";
        if (_batterySeconds.Value > 0f)
        {
            // Battery as a bar of ten segments + percent: ||||||····  63%
            int pct = Mathf.CeilToInt(_charge * 100f), seg = Mathf.Clamp(Mathf.CeilToInt(_charge * 10f - 0.001f), 0, 10);
            string col = _charge < Mathf.Clamp01(_minCharge.Value) ? "#e05a4f" : _charge < 0.35f ? "#e0c04f" : "#8fe08f";
            state += $"  <color={col}>{new string('|', seg)}</color><color=#ffffff40>{new string('|', 10 - seg)}</color> <color={col}>{pct}%</color>";
        }
        state = $"{name} [{key}]  {state}";
        if (_gadgetText.text != state) _gadgetText.SetText(state);
        if (Time.time >= _nextGadgetFit) { _nextGadgetFit = Time.time + 1f; PlaceGadgetText(); }
        if (Time.time >= _nextBoosterHide) { _nextBoosterHide = Time.time + 0.5f; HideBoosters(); }
    }

    /// <summary>The stock booster icons stay hidden (the game may switch them back on).</summary>
    private static void HideBoosters()
    {
        if (!_hideBoosterIcons.Value || _status == null) return;
        var d = _status.m_boosterIconActiveDisplay;
        if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
    }

    private static bool MakeGadgetText()
    {
        if (Time.time < _nextGadgetFind) return false;
        _nextGadgetFind = Time.time + 2f;
        var status = UnityEngine.Object.FindObjectOfType<PUI_LocalPlayerStatus>();
        var src = status?.m_healthText;
        if (src == null) return false;
        _status = status;
        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent, false);
        go.name = "GtfoPunk_GadgetHud";
        _gadgetText = go.GetComponent<TMPro.TextMeshPro>();
        if (_gadgetText == null) { UnityEngine.Object.Destroy(go); return false; }
        _gadgetText.enableWordWrapping = false;
        _gadgetText.alignment = TMPro.TextAlignmentOptions.Center;
        _gadgetText.alpha = Mathf.Clamp01(_gadgetHudAlpha.Value);
        _nextGadgetFit = 0f;
        HideBoosters();
        ModInfo.Log.LogInfo($"[Loadout] gadget line added above health (font {src.fontSize:0.#})");
        return true;
    }

    private static int _fitLogs;

    /// <summary>Centered just above the health readout; font scaled until the text is GadgetHudHeight of the screen tall.</summary>
    private static void PlaceGadgetText()
    {
        var health = _status?.m_healthText;
        var cam = UiCamera.For(_gadgetText.gameObject.layer);
        if (health == null || cam == null) return;
        var hr = health.GetComponent<Renderer>();
        var gr = _gadgetText.GetComponent<Renderer>();
        if (hr == null || gr == null) return;

        // Size: measure on screen, scale the font towards the wanted height.
        _gadgetText.ForceMeshUpdate();
        float want = Screen.height * Mathf.Clamp(_gadgetHudHeight.Value, 0.005f, 0.1f);
        float have = UiCamera.ScreenRect(cam, gr.bounds).height;
        if (have > 0.5f && Math.Abs(have - want) > want * 0.08f)
        {
            _gadgetText.fontSize *= Mathf.Clamp(want / have, 0.25f, 4f);
            _gadgetText.ForceMeshUpdate();
            if (_fitLogs++ < 4) ModInfo.Log.LogInfo($"[Loadout] gadget line: {have:0} px → {want:0} px tall, font {_gadgetText.fontSize:0.#}");
        }

        // Place: centered over the health readout, its bottom a little above the readout's top.
        var hs = UiCamera.ScreenRect(cam, hr.bounds);
        var gs = UiCamera.ScreenRect(cam, gr.bounds);
        var shift = TurretVariants.Vec(_gadgetHudOffset.Value) * Screen.height;
        var targetCenter = new Vector2(hs.center.x + shift.x, hs.yMax + want * 0.6f + gs.height * 0.5f + shift.y);
        float depth = cam.WorldToScreenPoint(gr.bounds.center).z;
        var worldNow = cam.ScreenToWorldPoint(new Vector3(gs.center.x, gs.center.y, depth));
        var worldWant = cam.ScreenToWorldPoint(new Vector3(targetCenter.x, targetCenter.y, depth));
        _gadgetText.transform.position += worldWant - worldNow;
    }
}
