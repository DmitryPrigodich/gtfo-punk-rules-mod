using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Gadget battery, like the Half-Life flashlight: drains while the gadget is on, recharges while it's off.
/// Empty — the gadget switches itself off; it won't switch on below GadgetMinCharge.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<float> _batterySeconds, _rechargeSeconds, _minCharge;

    private void BindBattery()
    {
        _batterySeconds = BindMoved(SecGadget, "BatterySeconds", 90f, "How long a full battery lasts while the gadget is on (s). 0 — no battery.", "GadgetBatterySeconds");
        _rechargeSeconds = BindMoved(SecGadget, "RechargeSeconds", 45f, "How long an empty battery takes to recharge while the gadget is off (s).", "GadgetRechargeSeconds");
        _minCharge = BindMoved(SecGadget, "MinCharge", 0.1f, "The gadget won't switch on with less charge than this (0–1).", "GadgetMinCharge");
    }

    private static float _charge = 1f;
    private static bool _chargeFullSaid = true;

    private static bool GadgetOn(PerkKind perk) => perk == PerkKind.Thermal ? _thermalOn : perk == PerkKind.BioTracker && _radarOn;

    /// <summary>May the gadget switch on now? (Beeps "no" if the battery is too low.)</summary>
    private static bool CanSwitchOn()
    {
        if (_batterySeconds.Value <= 0f || _charge >= Mathf.Clamp01(_minCharge.Value)) return true;
        try { CellSound.Post(AK.EVENTS.BIOTRACKER_NO_MOVING_TARGET_FOUND, GameUtil.LocalAgent.Position); } catch { }
        ModInfo.Log.LogInfo($"[Loadout] gadget battery too low: {_charge * 100f:0}%");
        return false;
    }

    private static void BatteryTick(bool inLevel, PerkKind perk)
    {
        if (!inLevel || _batterySeconds.Value <= 0f) { _charge = 1f; _chargeFullSaid = true; return; }
        if (GadgetOn(perk))
        {
            _charge = Math.Max(0f, _charge - Time.deltaTime / Math.Max(1f, _batterySeconds.Value));
            _chargeFullSaid = false;
            if (_charge > 0f) return;
            ModInfo.Log.LogInfo("[Loadout] gadget battery empty — switched off");
            try { CellSound.Post(AK.EVENTS.BIOTRACKER_NO_MOVING_TARGET_FOUND, GameUtil.LocalAgent.Position); } catch { }
            if (perk == PerkKind.Thermal) SetThermal(false);
            else SetRadar(false);
            return;
        }
        _charge = Math.Min(1f, _charge + Time.deltaTime / Math.Max(1f, _rechargeSeconds.Value));
        if (_charge >= 1f && !_chargeFullSaid)
        {
            _chargeFullSaid = true;
            try { CellSound.Post(AK.EVENTS.BIOTRACKER_RECHARGED, GameUtil.LocalAgent.Position); } catch { }
        }
    }
}
