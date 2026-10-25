using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Status light on every sentry (ours and the stock ones): green — fresh, turning yellow as the ammo / battery runs
/// down to half, orange below that, pulsing red — not working (empty or switched off).
/// </summary>
public partial class TurretVariants
{
    private static ConfigEntry<bool> _statusLight, _statusAll;
    private static ConfigEntry<string> _statusOffset;
    private static ConfigEntry<float> _statusRange, _statusIntensity;

    private void BindStatus()
    {
        _statusLight = Bind("StatusLight", true, "A small light on the sentry shows its state: green — full, yellow — half used, red — not working (empty / switched off).");
        _statusAll = Bind("StatusLightStockSentries", true, "The status light on the game's own sentries too (not only on ours).");
        _statusOffset = Bind("StatusLightOffset", "0,0.45,0", "Where the status light sits, relative to the sentry's base (x,y,z m; y — up).");
        _statusRange = Bind("StatusLightRange", 1.2f, "Status light range (m).");
        _statusIntensity = Bind("StatusLightIntensity", 1.5f, "Status light brightness.");
    }

    private class Status { public SentryGunInstance Inst; public GameObject Go; public Light Light; public CL_Light Cl; }

    private static readonly Dictionary<IntPtr, Status> _status = new();
    private static float _nextStatus;

    private static void AddStatusLight(SentryGunInstance inst)
    {
        if (!_statusLight.Value || inst == null || _status.ContainsKey(inst.Pointer)) return;
        var go = new GameObject("GtfoPunk_StatusLight");
        go.transform.SetParent(inst.transform, false);
        go.transform.localPosition = Vec(_statusOffset.Value);
        var light = MakeLight(go, LightType.Point, _statusRange.Value, _statusIntensity.Value, Color.green);
        _status[inst.Pointer] = new Status { Inst = inst, Go = go, Light = light, Cl = go.GetComponent<CL_Light>() };
    }

    private static void RemoveStatusLight(SentryGunInstance inst)
    {
        if (inst == null || !_status.TryGetValue(inst.Pointer, out var s)) return;
        if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
        _status.Remove(inst.Pointer);
    }

    private static readonly List<IntPtr> _statusDead = new();

    private static void StatusTick()
    {
        if (_status.Count == 0 || Time.time < _nextStatus) return;
        _nextStatus = Time.time + 0.1f;
        _statusDead.Clear();
        foreach (var kv in _status)
        {
            var s = kv.Value;
            if (s.Inst == null || s.Go == null || s.Light == null) { _statusDead.Add(kv.Key); continue; }
            bool ours = _turrets.TryGetValue(kv.Key, out var t);
            bool show = _statusLight.Value && (ours || _statusAll.Value);
            if (s.Light.enabled != show) s.Light.enabled = show;
            if (!show) continue;

            float cap = s.Inst.AmmoMaxCap;
            float frac = cap > 0f ? Mathf.Clamp01(s.Inst.m_ammo / cap) : 0f;
            bool dead = (ours && t.Flat) || frac <= 0.001f || (!ours && s.Inst.m_ammo < s.Inst.CostOfBullet);
            Color c;
            float k = 1f;
            if (dead) { c = new Color(1f, 0.05f, 0.03f); k = 0.55f + 0.45f * Mathf.Sin(Time.time * 4f); }
            else if (frac >= 0.5f) c = Color.Lerp(new Color(1f, 0.85f, 0.05f), new Color(0.15f, 1f, 0.2f), (frac - 0.5f) * 2f);
            else c = Color.Lerp(new Color(1f, 0.45f, 0.05f), new Color(1f, 0.85f, 0.05f), frac * 2f);
            s.Light.color = c;
            s.Light.intensity = _statusIntensity.Value * k;
            s.Light.range = _statusRange.Value;
            var pos = Vec(_statusOffset.Value);
            bool moved = s.Go.transform.localPosition != pos;
            if (moved) s.Go.transform.localPosition = pos;
            // GTFO draws lights through its clustered system — push the new colour / position to it.
            try { if (moved) s.Cl?.UpdateTransform(); s.Cl?.UpdateData(); } catch { }
        }
        foreach (var k in _statusDead) _status.Remove(k);
    }
}
