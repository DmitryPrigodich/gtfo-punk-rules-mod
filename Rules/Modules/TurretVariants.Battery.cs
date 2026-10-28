using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Battery of the non-shooting sentries (their ammo drains while they work, at zero they switch off) and the
/// sentry screen: "TL1 87%", "TF I OFF", "TB1 [3] 64%", "TCF3 75%" (tank), "TGL3 21" (grenades left).
/// </summary>
public partial class TurretVariants
{
    /// <summary>Drains the ammo as a battery (same rate on every machine; the host's value is synced anyway) and switches the sentry off/on.</summary>
    private static void Battery(Turret t)
    {
        var seconds = t.Kind.BatterySeconds;
        if (!_battery.Value || seconds == null) { if (t.Flat) PowerOn(t); return; }
        float cap = t.Inst.AmmoMaxCap;
        if (seconds.Value > 0f && cap > 0f && t.Inst.m_ammo > 0f)
            t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - cap / seconds.Value * Time.deltaTime);
        bool empty = t.Inst.m_ammo <= 0.0001f;
        if (empty && !t.Flat) PowerOff(t);
        else if (!empty && t.Flat) PowerOn(t);
    }

    private static void PowerOff(Turret t)
    {
        t.Flat = true;
        SetLights(t, false);
        try { t.Sphere?.StopRepelling(); } catch { }
        try { CellSound.Post(AK.EVENTS.BIOTRACKER_TOOL_LOOP_STOP, t.Inst.transform.position); } catch { }
        ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value}: battery empty — switched off");
    }

    private static void PowerOn(Turret t)
    {
        t.Flat = false;
        SetLights(t, true);
        try { if (t.Sphere != null) { t.Sphere.InfiniteDuration = true; t.Sphere.StartRepelling(); } } catch { }
    }

    private static void SetLights(Turret t, bool on)
    {
        if (t.Light != null) t.Light.enabled = on;
        if (t.Glow != null) t.Glow.enabled = on;
        if (t.Bulb != null) t.Bulb.enabled = on && t.Kind.Bulb.Value > 0f;
    }

    private static void UpdateScreen(Turret t)
    {
        if (t.Screen == null)
        {
            var scr = t.Inst.m_screen == null ? null : ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)t.Inst.m_screen).TryCast<SentryGunScreen>();
            t.Screen = scr?.m_ammoText;
            if (t.Screen == null) return;
            try
            {
                // One line, shrinking to fit (long labels used to wrap and push the text up).
                float size = t.Screen.fontSize;
                t.Screen.enableWordWrapping = false;
                t.Screen.enableAutoSizing = true;
                t.Screen.fontSizeMax = size;
                t.Screen.fontSizeMin = size * 0.3f;
            }
            catch { }
        }
        string text = t.Kind.Label.Value;
        if (t.Mode == Mode.Tracker && t.Tagged > 0) text += $" [{t.Tagged}]";
        float cap = t.Inst.AmmoMaxCap;
        int percent = cap > 0f ? Mathf.CeilToInt(100f * t.Inst.m_ammo / cap) : 0;
        if (t.Mode == Mode.Grenade)
        {
            float cost = GrenadeCost(t);
            if (cost > 0f) text += $" {Mathf.FloorToInt(t.Inst.m_ammo / cost + 0.001f)}"; // a counted weapon: grenades left
        }
        else if (t.Mode == Mode.Foam) { if (cap > 0f) text += $" {percent}%"; } // a tank
        else if (_battery.Value && t.Kind.BatterySeconds != null && cap > 0f) text += t.Flat ? " OFF" : $" {percent}%";
        if (t.Screen.text != text) t.Screen.text = text;
    }
}
