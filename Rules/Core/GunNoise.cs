using Agents;
using Enemies;
using Gear;
using Il2CppInterop.Runtime.InteropTypes;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>How loud a gun is: the game's own shot noise or none (Silent), plus noise of its own around the shooter.</summary>
public struct NoiseSpec
{
    /// <summary>No game shot noise — only the radii below.</summary>
    public bool Silent;
    /// <summary>Within this distance (m) a shot wakes sleepers; 0 — never.</summary>
    public float Wake;
    /// <summary>Up to this distance (m) sleepers stir; 0 — not at all.</summary>
    public float Stir;
    /// <summary>Stirred enemies may wake after shots in a row (regular noise instead of a pulse).</summary>
    public bool StirWakes;
    /// <summary>Heard through walls and closed doors.</summary>
    public bool ThroughWalls;
}

/// <summary>
/// Per-gun shot noise: the guns register their category with a <see cref="NoiseSpec"/>. On a shot (the shooter's own and
/// the synced copy on the host) the game's noise is muted for a silent gun, and the host makes ours. Everyone needs the mod.
/// </summary>
public static class GunNoise
{
    private static readonly Dictionary<uint, NoiseSpec> _cats = new();
    private static readonly Dictionary<IntPtr, float> _cooldown = new();
    private static bool _installed;

    public static void Set(uint category, NoiseSpec n) { if (category != 0) _cats[category] = n; }
    public static bool TryGet(uint category, out NoiseSpec n) => _cats.TryGetValue(category, out n);

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        var h = new Harmony(ModInfo.Guid + ".GunNoise");
        h.Patch(AccessTools.Method(typeof(BulletWeapon), nameof(BulletWeapon.Fire)), postfix: new HarmonyMethod(typeof(GunNoise), nameof(OnFire)));
        h.Patch(AccessTools.Method(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire)), postfix: new HarmonyMethod(typeof(GunNoise), nameof(OnFireSynced)));
        FrameTicker.OnLevelExit += () => _cooldown.Clear();
    }

    private static void OnFireSynced(BulletWeaponSynced __instance) => OnFire(__instance);

    private static void OnFire(BulletWeapon __instance)
    {
        var w = __instance;
        if (w == null || _cats.Count == 0) return;
        NoiseSpec n;
        try { var g = w.GearIDRange; if (g == null || !_cats.TryGetValue(g.GetCompID(eGearComponent.Category), out n)) return; }
        catch { return; }
        var owner = w.Owner;
        if (owner == null) return;
        if (n.Silent) owner.m_noise = Agent.NoiseType.Silent; // the stock "loud shot" isn't needed
        if (!SNet.IsMaster || (n.Wake <= 0f && n.Stir <= 0f)) return;
        if (_cooldown.TryGetValue(owner.Pointer, out var t) && Time.time < t) return;
        _cooldown[owner.Pointer] = Time.time + 0.4f;
        try { MakeNoise(owner, n); } catch (Exception e) { ModInfo.Log.LogWarning($"[GunNoise] {e.Message}"); }
    }

    private static void MakeNoise(PlayerAgent owner, NoiseSpec n)
    {
        float wake = Math.Max(0f, n.Wake), stir = Math.Max(n.Stir, wake);
        if (stir <= 0f) return;
        var maker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>();
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(owner.Position, stir, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var limb = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>();
            var enemy = limb?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var node = enemy.CourseNode;
            if (node == null) continue;
            if (!n.ThroughWalls)
            {
                // Not heard through walls and closed doors.
                if (Physics.Linecast(owner.EyePosition, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
                if (node.m_zone.LocalIndex != GameData.eLocalZoneIndex.Zone_0 && node.m_zone.m_sourceGate != null && !node.m_zone.m_sourceGate.HasBeenOpenedDuringPlay) continue;
            }
            bool woke = Vector3.Distance(owner.EyePosition, enemy.EyePosition) <= wake;
            NoiseManager.MakeNoise(new NM_NoiseData
            {
                noiseMaker = maker,
                position = enemy.Position,
                node = node,
                radiusMin = 0.01f,
                radiusMax = 1f,
                yScale = 1f,
                type = woke ? NM_NoiseType.InstaDetect : (n.StirWakes ? NM_NoiseType.Detectable : NM_NoiseType.PulseOnly),
                includeToNeightbourAreas = false,
                raycastFirstNode = false,
            });
        }
    }
}
