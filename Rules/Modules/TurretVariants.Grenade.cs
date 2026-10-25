using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>TGL3 grenade launcher: bursts of contact grenades (module Grenades), led onto moving targets, never near players.</summary>
public partial class TurretVariants
{
    private static bool SetupGrenade(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f;
        AttachParts(t, head.transform, _glParts.Value);
        return true;
    }

    private static bool _glLogged;

    /// <summary>Grenades one shot costs, in the sentry's ammo.</summary>
    private static float GrenadeCost(Turret t) => Math.Max(0f, t.Inst.CostOfBullet * _glAmmo.Value);

    private static void GrenadeTick(Turret t)
    {
        TrackAim(t);
        int burst = Math.Max(1, _glBurst.Value);
        if (t.MagLeft < 0) t.MagLeft = burst;
        // Idle with a partly used burst — top it up after the reload time.
        if (t.MagLeft < burst && t.MagLeft > 0 && Time.time - t.LastShotAt > Math.Max(0.5f, _glReload.Value)) t.MagLeft = burst;
        if (Time.time < t.NextShot) return;
        if (t.MagLeft == 0) t.MagLeft = burst; // reload finished (NextShot was pushed back by the reload time)
        var det = t.Inst.m_detection;
        if (det == null || !det.HasTarget || !t.Inst.TryGetTargetAimPos(out var aim)) return;
        var owner = t.Inst.Owner;
        if (owner == null || t.Inst.m_ammo < GrenadeCost(t)) return;

        var muzzle = Firing(t)?.MuzzleAlign;
        var from = muzzle != null ? muzzle.position + muzzle.forward * 0.5f : t.Inst.transform.position + Vector3.up * 0.8f;
        float dist = Vector3.Distance(from, aim);
        if (dist > _glRange.Value || dist < _glMinRange.Value) return;

        float speed = Math.Max(1f, _glSpeed.Value), g = Math.Abs(Physics.gravity.y);
        var target = Lead(from, aim, t.AimVel, speed, g);
        // Don't blow up our own: no players (and not the sentry itself) near the impact point.
        float safe = Math.Max(0f, _glSafe.Value);
        if (Vector3.Distance(target, t.Inst.transform.position) < safe) return;
        foreach (var p in PlayerManager.PlayerAgentsInLevel)
            if (p != null && p.Alive && Vector3.Distance(p.Position, target) < safe) { t.NextShot = Time.time + 0.5f; return; }

        if (!Grenades.Throw(owner, from, Ballistic(from, target, speed, g), owner.CourseNode ?? t.Inst.CourseNode))
        {
            if (!_glLogged) { _glLogged = true; ModInfo.Log.LogWarning($"[TurretVariants] {t.Kind.Label.Value}: no grenade model — won't fire"); }
            t.NextShot = Time.time + 5f;
            return;
        }
        try { CellSound.Post(AK.EVENTS.GLUEGUNFIRE1SHOT, from); } catch { }
        t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - GrenadeCost(t));
        t.MagLeft--;
        t.LastShotAt = Time.time;
        t.NextShot = Time.time + (t.MagLeft > 0 ? Math.Max(0.1f, _glShotDelay.Value) : Math.Max(0.5f, _glReload.Value));
        if (t.MagLeft == 0) try { CellSound.Post(AK.EVENTS.GLUEGUNRECHARGE, t.Inst.transform.position); } catch { }
    }
}
