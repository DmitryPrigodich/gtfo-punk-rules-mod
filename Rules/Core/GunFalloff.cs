using Gear;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// The shape of a gun's damage falloff. The game keeps full damage up to FalloffFrom and then lowers it in a straight
/// line to FalloffTo (Linear). Square — (1 − t)², falls faster right after FalloffFrom; Hyperbola — FalloffFrom / distance,
/// a long tail that never quite reaches zero (FalloffTo isn't used). For a non-linear gun the hit's damage is set here and
/// the game's own falloff is switched off for that hit. Damage is counted on the shooter's machine.
/// </summary>
public static class GunFalloff
{
    public enum Curve { Linear, Square, Hyperbola }

    private static readonly Dictionary<uint, Curve> _cats = new();
    private static bool _installed;

    public static void Set(uint category, Curve c) { if (category == 0) return; if (c == Curve.Linear) _cats.Remove(category); else _cats[category] = c; }
    public static Curve Of(uint category) => _cats.TryGetValue(category, out var c) ? c : Curve.Linear;

    public static Curve Parse(string s) => Enum.TryParse<Curve>((s ?? "").Trim(), true, out var c) ? c : Curve.Linear;

    /// <summary>The damage share at a distance (1 — full).</summary>
    public static float Multiplier(Curve c, Vector2 falloff, float distance)
    {
        float from = Math.Max(0.01f, falloff.x), to = Math.Max(from + 0.01f, falloff.y);
        if (distance <= from) return 1f;
        switch (c)
        {
            case Curve.Hyperbola: return from / distance;
            case Curve.Square: { float t = Mathf.Clamp01((distance - from) / (to - from)); return (1f - t) * (1f - t); }
            default: return Mathf.Clamp01(1f - (distance - from) / (to - from));
        }
    }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        var h = new Harmony(ModInfo.Guid + ".GunFalloff");
        h.Patch(AccessTools.Method(typeof(BulletWeapon), nameof(BulletWeapon.BulletHit)),
            prefix: new HarmonyMethod(typeof(GunFalloff), nameof(Prefix)), postfix: new HarmonyMethod(typeof(GunFalloff), nameof(Postfix)));
    }

    private static readonly Vector2 NoFalloff = new(100000f, 100001f);

    // The hit data is reused for the next hits of a piercing round: set it for this hit, put it back after.
    private static void Prefix(Weapon.WeaponHitData weaponRayData, float additionalDis, out (bool On, float Damage, Vector2 Falloff) __state)
    {
        __state = default;
        try
        {
            if (weaponRayData == null || _cats.Count == 0 || !_cats.TryGetValue(weaponRayData.gearCategoryId, out var c)) return;
            float dist = weaponRayData.rayHit.distance + additionalDis;
            __state = (true, weaponRayData.damage, weaponRayData.damageFalloff);
            weaponRayData.damage *= Multiplier(c, weaponRayData.damageFalloff, dist);
            weaponRayData.damageFalloff = NoFalloff;
        }
        catch { __state = default; }
    }

    private static void Postfix(Weapon.WeaponHitData weaponRayData, (bool On, float Damage, Vector2 Falloff) __state)
    {
        if (!__state.On || weaponRayData == null) return;
        try { weaponRayData.damage = __state.Damage; weaponRayData.damageFalloff = __state.Falloff; } catch { }
    }
}
