using GameData;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// TB1 bio-tracker: scans its sector and tags enemies like a handheld bio-tracker.
/// Tagging pays off: combat sentries get the game's own tagged-target bonuses, scaled up here.
/// </summary>
public partial class TurretVariants
{
    private static bool SetupTracker(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f; // no barrel — scan without parts
        AttachParts(t, head.transform, _trackerParts.Value);
        return true;
    }

    private static void TrackerTick(Turret t)
    {
        if (Time.time < t.NextScan) return;
        t.NextScan = Time.time + Math.Max(0.5f, _trackerInterval.Value);
        var origin = t.Inst.transform.position + Vector3.up * 0.6f;
        var fwd = t.Inst.transform.forward;
        fwd.y = 0f;
        float half = _trackerAngle.Value * 0.5f;
        int count = 0, tagged = 0;
        foreach (var e in EnemiesNear(origin, _trackerRange.Value))
        {
            var to = e.Position - origin;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > half) continue; // outside the sector
            if (!_trackerWalls.Value && Physics.Linecast(origin, e.EyePosition, LayerManager.MASK_WORLD)) continue;
            count++;
            if (e.IsTagged || !SNetwork.SNet.IsMaster) continue;
            ToolSyncManager.WantToTagEnemy(e);
            tagged++;
        }
        t.Tagged = count;
        // Like the handheld bio-tracker: a beep when someone is in the sector (for all players).
        if (count > 0 && _trackerSound.Value) CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, origin);
        if (tagged > 0) ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value}: in sector {count}, newly tagged {tagged}");
    }

    private static bool _tagBonusApplied;

    /// <summary>
    /// Every sentry archetype already has "tagged target" multipliers (damage, stagger, turning speed, start-fire
    /// delay, priority) — mostly left at 1. Scale them so tagging pays off. Works for any tag (TB1, bio-tracker).
    /// </summary>
    private static void ApplyTagBonus()
    {
        if (_tagBonusApplied) return;
        _tagBonusApplied = true;
        var done = new HashSet<uint>();
        foreach (eWeaponFireMode mode in Enum.GetValues(typeof(eWeaponFireMode)))
        {
            if (!mode.ToString().StartsWith("Sentry")) continue;
            ArchetypeDataBlock a = null;
            try { a = SentryGunInstance_Firing_Bullets.GetArchetypeDataForFireMode(mode); } catch { }
            if (a == null || !done.Add(a.persistentID)) continue;
            string before = $"dmg ×{a.Sentry_DamageTagMulti:0.##}, turn ×{a.Sentry_RotationSpeedTagMulti:0.##}, start delay ×{a.Sentry_StartFireDelayTagMulti:0.##}, prio {a.Sentry_PrioTag}";
            a.Sentry_DamageTagMulti *= Math.Max(0.1f, _tagDamage.Value);
            a.Sentry_StaggerDamageTagMulti *= Math.Max(0.1f, _tagDamage.Value);
            a.Sentry_RotationSpeedTagMulti *= Math.Max(0.1f, _tagTurn.Value);
            a.Sentry_StartFireDelayTagMulti *= Mathf.Clamp(_tagDelay.Value, 0.05f, 10f);
            if (_tagPrio.Value) a.Sentry_PrioTag = true;
            ModInfo.Log.LogInfo($"[TurretVariants] tagged-target bonus, {a.name} ({mode}): {before} → dmg ×{a.Sentry_DamageTagMulti:0.##}, turn ×{a.Sentry_RotationSpeedTagMulti:0.##}, start delay ×{a.Sentry_StartFireDelayTagMulti:0.##}, prio {a.Sentry_PrioTag}");
        }
    }
}
