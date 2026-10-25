using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// A semi-transparent line above the health readout: which gadget you carry and its state —
/// "BIO-TRACKER [N] ready / 5 s / 3 found", "THERMAL [N] on ×0.5 / off".
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<bool> _gadgetHud;
    private static ConfigEntry<float> _gadgetHudAlpha;
    private static ConfigEntry<string> _gadgetHudOffset;
    private static ConfigEntry<float> _gadgetHudSize;
    private static ConfigEntry<bool> _hideBoosterIcons;

    private void BindGadgetHud()
    {
        _gadgetHud = Bind("GadgetHud", true, "Show the gadget and its state (ready / recharging / found, on / off) above the health readout.");
        _gadgetHudAlpha = Bind("GadgetHudOpacity", 0.6f, "Opacity of the gadget line (0–1).");
        _gadgetHudOffset = Bind("GadgetHudShift", "0,0", "Shift of the gadget line from its place (where the stock booster icons were, above health), x,y in HUD units; up is +y.");
        _gadgetHudSize = Bind("GadgetHudSize", 0.8f, "Gadget line text size, relative to the health readout.");
        _hideBoosterIcons = Bind("HideBoosterIcons", true, "Hide the stock booster icons above health (boosters are replaced by the loadout); the gadget line takes their place.");
    }

    private static TMPro.TextMeshPro _gadgetText;

    private static void GadgetHudTick(bool inLevel, PerkKind perk)
    {
        bool show = _gadgetHud.Value && inLevel && perk != PerkKind.None && GameUtil.LocalAgent != null;
        if (!show) { if (_gadgetText != null) _gadgetText.gameObject.SetActive(false); return; }
        if (_gadgetText == null && !MakeGadgetText()) return;
        if (!_gadgetText.gameObject.activeSelf) _gadgetText.gameObject.SetActive(true);

        string key = _gadgetKey.Value.ToUpperInvariant();
        bool on = GadgetOn(perk);
        string name = perk == PerkKind.BioTracker ? Lang.T("BIO-TRACKER", "БИОТРЕКЕР") : Lang.T("THERMAL", "ТЕПЛОВИЗОР");
        string state = on ? $"<color=#ffb347>{Lang.T("on", "вкл")}</color>" : Lang.T("off", "выкл");
        if (on && perk == PerkKind.Thermal && _zoom > 0.01f) state += $" ×{1f / (1f - _zoom):0.#}";
        if (_batterySeconds.Value > 0f)
        {
            // Battery as a bar of ten segments + percent: ||||||····  63%
            int pct = Mathf.CeilToInt(_charge * 100f), seg = Mathf.CeilToInt(_charge * 10f - 0.001f);
            string col = _charge < Mathf.Clamp01(_minCharge.Value) ? "#e05a4f" : _charge < 0.35f ? "#e0c04f" : "#8fe08f";
            state += $"  <color={col}>{new string('|', seg)}</color><color=#ffffff40>{new string('|', 10 - seg)}</color> <color={col}>{pct}%</color>";
        }
        state = $"{name} [{key}]  {state}";
        if (_gadgetText.text != state) _gadgetText.SetText(state);
        if (Time.time >= _nextBoosterHide) { _nextBoosterHide = Time.time + 0.5f; HideBoosters(); }
    }

    private static float _nextBoosterHide;
    private static PUI_LocalPlayerStatus _status;

    /// <summary>The stock booster icons stay hidden (the game may switch them back on).</summary>
    private static void HideBoosters()
    {
        if (!_hideBoosterIcons.Value || _status == null) return;
        var d = _status.m_boosterIconActiveDisplay;
        if (d != null && d.gameObject.activeSelf) d.gameObject.SetActive(false);
    }

    private static float _nextGadgetFind;

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
        var off = TurretVariants.Vec(_gadgetHudOffset.Value);
        // In place of the stock booster icons (above health); without them — a line above the health readout.
        var boosters = status.m_boosterIconActiveDisplay;
        if (boosters != null)
        {
            go.transform.position = boosters.transform.position;
            go.transform.localPosition += new Vector3(off.x, off.y, 0f);
        }
        else go.transform.localPosition = src.transform.localPosition + new Vector3(off.x, 40f + off.y, 0f);
        _gadgetText = go.GetComponent<TMPro.TextMeshPro>();
        if (_gadgetText == null) { UnityEngine.Object.Destroy(go); return false; }
        _gadgetText.enableWordWrapping = false;
        _gadgetText.fontSize = src.fontSize * Mathf.Clamp(_gadgetHudSize.Value, 0.2f, 2f);
        _gadgetText.alpha = Mathf.Clamp01(_gadgetHudAlpha.Value);
        HideBoosters();
        ModInfo.Log.LogInfo($"[Loadout] gadget line added {(boosters != null ? "in place of the booster icons" : "above health")} (font {src.fontSize:0.#} → {_gadgetText.fontSize:0.#}, alignment {src.alignment})");
        return true;
    }
}
