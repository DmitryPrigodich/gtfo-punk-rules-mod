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

    private void BindGadgetHud()
    {
        _gadgetHud = Bind("GadgetHud", true, "Show the gadget and its state (ready / recharging / found, on / off) above the health readout.");
        _gadgetHudAlpha = Bind("GadgetHudOpacity", 0.6f, "Opacity of the gadget line (0–1).");
        _gadgetHudOffset = Bind("GadgetHudOffset", "0,38", "Position of the gadget line relative to the health readout (x,y in HUD units; up is +y).");
    }

    private static TMPro.TextMeshPro _gadgetText;
    private static int _lastFound = -1;
    private static float _lastFoundAt = -10f;

    private static void GadgetHudTick(bool inLevel, PerkKind perk)
    {
        bool show = _gadgetHud.Value && inLevel && perk != PerkKind.None && GameUtil.LocalAgent != null;
        if (!show) { if (_gadgetText != null) _gadgetText.gameObject.SetActive(false); return; }
        if (_gadgetText == null && !MakeGadgetText()) return;
        if (!_gadgetText.gameObject.activeSelf) _gadgetText.gameObject.SetActive(true);

        string key = _gadgetKey.Value.ToUpperInvariant();
        string state;
        if (perk == PerkKind.BioTracker)
        {
            float left = _nextScan - Time.time;
            if (Time.time - _lastFoundAt < 2.5f && _lastFound >= 0)
                state = _lastFound > 0 ? $"<color=#ff8a5c>{Lang.T("found", "найдено")} {_lastFound}</color>" : Lang.T("nothing", "пусто");
            else if (_bioAuto.Value) state = Lang.T("auto", "авто");
            else state = left > 0f ? $"{Mathf.CeilToInt(left)} {Lang.T("s", "с")}" : $"<color=#8fe08f>{Lang.T("ready", "готов")}</color>";
            state = $"{Lang.T("BIO-TRACKER", "БИОТРЕКЕР")} [{key}]  {state}";
        }
        else
        {
            string on = _thermalOn ? $"<color=#ffb347>{Lang.T("on", "вкл")}</color>" + (_zoom > 0.01f ? $" ×{1f / (1f - _zoom):0.#}" : "") : Lang.T("off", "выкл");
            state = $"{Lang.T("THERMAL", "ТЕПЛОВИЗОР")} [{key}]  {on}";
        }
        if (_gadgetText.text != state) _gadgetText.SetText(state);
    }

    private static float _nextGadgetFind;

    private static bool MakeGadgetText()
    {
        if (Time.time < _nextGadgetFind) return false;
        _nextGadgetFind = Time.time + 2f;
        var status = UnityEngine.Object.FindObjectOfType<PUI_LocalPlayerStatus>();
        var src = status?.m_healthText;
        if (src == null) return false;
        var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent, false);
        go.name = "GtfoPunk_GadgetHud";
        var off = TurretVariants.Vec(_gadgetHudOffset.Value);
        go.transform.localPosition = src.transform.localPosition + new Vector3(off.x, off.y, 0f);
        _gadgetText = go.GetComponent<TMPro.TextMeshPro>();
        if (_gadgetText == null) { UnityEngine.Object.Destroy(go); return false; }
        _gadgetText.enableWordWrapping = false;
        _gadgetText.fontSize = src.fontSize * 0.55f;
        _gadgetText.alpha = Mathf.Clamp01(_gadgetHudAlpha.Value);
        ModInfo.Log.LogInfo($"[Loadout] gadget line added above health (font {src.fontSize:0.#} → {_gadgetText.fontSize:0.#}, alignment {src.alignment})");
        return true;
    }
}
