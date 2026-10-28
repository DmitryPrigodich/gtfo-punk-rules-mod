using Enemies;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// TCF3 foam sentry: lobs C-foam globs (bursts, leading moving targets) instead of bullets. Near a TCF3 the foam
/// slows enemies instead of freezing them, is acidic and staggers (interrupts attacks).
/// </summary>
public partial class TurretVariants
{
    private static bool SetupFoam(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f; // no barrel — shoot without models
        AttachParts(t, head.transform, _foamParts.Value);
        var prefab = TankPrefab();
        if (prefab == null) return true;
        try
        {
            var right = CopyModel(prefab, head.transform, "GtfoPunk_FoamTankR");
            var left = CopyModel(prefab, head.transform, "GtfoPunk_FoamTankL");
            void Place()
            {
                var off = Vec(_tankOffset.Value);
                var rot = Quaternion.Euler(Vec(_tankRotation.Value));
                var scale = Vector3.one * _tankScale.Value;
                if (right != null) { right.transform.localPosition = off; right.transform.localRotation = rot; right.transform.localScale = scale; }
                if (left != null)
                {
                    left.SetActive(_tankMirror.Value); // the mirrored second tank
                    left.transform.localPosition = new Vector3(-off.x, off.y, off.z);
                    left.transform.localRotation = rot;
                    left.transform.localScale = scale;
                }
            }
            Place();
            t.Reapply.Add(Place);
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] {t.Kind.Label.Value} tanks: {e.Message}"); }
        return true;
    }

    /// <summary>The tank model: the fog turbine (its item has no meshes of its own — found in memory) or an item's model.</summary>
    private static GameObject TankPrefab()
    {
        string name = _tankItem.Value?.Trim() ?? "";
        if (name.Length == 0) return null;
        if (name.ToLowerInvariant().Contains("turbine")) return ItemModel(FogTurbineItemId) ?? FindLoadedModel(TurbineModelSearch);
        return ItemModel(FindItemId(name));
    }

    // ---------- shooting (host) ----------

    private static bool _foamLogged;

    private static void FoamTick(Turret t)
    {
        TrackAim(t);
        if (t.BurstLeft > 0)
        {
            if (Time.time < t.NextBurst) return;
            if (!FoamShot(t)) { t.BurstLeft = 0; return; }
            t.BurstLeft--;
            t.NextBurst = Time.time + Math.Max(0.03f, _foamBurstDelay.Value);
            return;
        }
        if (Time.time < t.NextShot || !FoamShot(t)) return;
        t.BurstLeft = Math.Max(0, _foamBurst.Value - 1);
        t.NextBurst = Time.time + Math.Max(0.03f, _foamBurstDelay.Value);
        t.NextShot = Time.time + Math.Max(0.2f, _foamInterval.Value);
    }

    /// <summary>One glob. false — nothing to shoot at or nothing to shoot with.</summary>
    private static bool FoamShot(Turret t)
    {
        var owner = t.Inst.Owner;
        if (owner == null) return false;
        float cost = Math.Max(0f, t.Inst.CostOfBullet * _foamAmmo.Value);
        if (!_foamLogged) { _foamLogged = true; ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value}: ammo {t.Inst.Ammo:0.##}/{t.Inst.AmmoMaxCap:0.##}, glob cost {cost:0.###}"); }
        if (t.Inst.m_ammo < cost) return false;

        var muzzle = Firing(t)?.MuzzleAlign;
        var from = muzzle != null ? muzzle.position + muzzle.forward * 0.3f : t.Inst.transform.position + Vector3.up * 0.7f;

        // The sentry's own target unless it's glued enough; otherwise the nearest unglued enemy in line of sight.
        Vector3 aim = default, vel = Vector3.zero;
        bool have = false;
        var det = t.Inst.m_detection;
        if (det != null && det.HasTarget && t.Inst.TryGetTargetAimPos(out var a))
        {
            var e = det.Target == null ? null : det.Target.GetComponentInParent<EnemyAgent>();
            if (!(e != null && _foamSkipGlued.Value && Glued(e)) && Vector3.Distance(from, a) <= _foamRange.Value) { aim = a; vel = t.AimVel; have = true; }
        }
        if (!have)
        {
            var e = NearestUnglued(from, muzzle != null ? muzzle.forward : t.Inst.transform.forward);
            if (e == null) return false;
            aim = e.EyePosition + Vector3.down * 0.4f; // center mass
        }

        float speed = Math.Max(1f, _foamSpeed.Value), g = Math.Max(0f, _foamGravity.Value);
        var target = _foamLead.Value ? Lead(from, aim, vel, speed, g) : aim;
        ProjectileManager.WantToFireGlue(owner, from, Ballistic(from, target, speed, g), _foamExpand.Value, true);
        try { CellSound.Post(AK.EVENTS.GLUEGUNFIRE1SHOT, from); } catch { }
        t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - cost);
        return true;
    }

    /// <summary>Glued enough: stuck, or (with the slowing foam) at the glue cap already — no point adding more.</summary>
    private static bool Glued(EnemyAgent e)
    {
        var d = e.Damage;
        if (d == null) return false;
        if (d.IsStuckInGlue) return true;
        float cap = _foamMaxGlue.Value;
        return cap < 1f && d.AttachedGlueRel >= cap * 0.9f;
    }

    private static EnemyAgent NearestUnglued(Vector3 from, Vector3 fwd)
    {
        EnemyAgent best = null;
        float bestD = float.MaxValue;
        foreach (var e in EnemiesNear(from, _foamRange.Value))
        {
            if (Glued(e)) continue;
            var to = e.EyePosition - from;
            if (Vector3.Angle(fwd, to) > 75f) continue; // the sentry doesn't shoot backwards
            if (Physics.Linecast(from, e.EyePosition, LayerManager.MASK_WORLD)) continue;
            float d = to.sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    // ---------- what the foam does near a TCF3 ----------

    private static float _nextFoamFx, _nextAcid;
    private static readonly Dictionary<IntPtr, float> _nextStagger = new();
    private static int _foamFxLogs;

    /// <summary>
    /// Enemies with foam on them near a TCF3:
    ///  - are kept below "stuck" (MaxGlue) — slowed, not frozen (every machine: glue is simulated locally);
    ///  - take acid damage (AcidDps, host);
    ///  - are staggered now and then, which interrupts attacks (Stagger, host).
    /// </summary>
    private static void FoamEffects()
    {
        if (Time.time < _nextFoamFx) return;
        _nextFoamFx = Time.time + 0.1f;
        bool master = SNetwork.SNet.IsMaster;
        bool acidTick = master && Time.time >= _nextAcid;
        if (acidTick) _nextAcid = Time.time + 0.5f;
        var seen = new HashSet<IntPtr>();
        foreach (var t in _turrets.Values)
        {
            if (t.Mode != Mode.Foam || !t.Ready || t.Inst == null) continue;
            foreach (var e in EnemiesNear(t.Inst.transform.position, _foamRange.Value + 5f))
            {
                var d = e.Damage;
                if (d == null || !seen.Add(e.Pointer)) continue;
                float rel = d.AttachedGlueRel;
                if (rel <= 0.02f) continue;

                float cap = _foamMaxGlue.Value;
                if (cap < 1f && rel > cap)
                {
                    try { d.RemoveGlueVolume(d.AttachedGlueVolume * (rel - cap) / rel, 0f); } catch { }
                    if (_foamFxLogs++ < 6) ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value} foam: glue {rel:0.##} → cap {cap:0.##} (stuck {d.IsStuckInGlue}, speed ×{d.GlueMoveSpeedMulti:0.##})");
                }
                if (!master) continue;
                var owner = t.Inst.Owner;
                int limb = BodyLimb(d); // a plain body limb, so weak spots don't multiply the acid
                if (acidTick && _foamAcid.Value > 0f)
                    try { d.BulletDamage(_foamAcid.Value * 0.5f, owner, e.EyePosition, Vector3.down, Vector3.up, false, limb, 0f, 1f, 0u); } catch { }
                if (_foamStagger.Value > 0f && (!_nextStagger.TryGetValue(e.Pointer, out var at) || Time.time >= at))
                {
                    _nextStagger[e.Pointer] = Time.time + Math.Max(0.5f, _foamStaggerInterval.Value);
                    try { d.BulletDamage(0.01f, owner, e.EyePosition, t.Inst.transform.forward, -t.Inst.transform.forward, false, limb, _foamStagger.Value, 1f, 0u); } catch { }
                }
            }
        }
    }

    private static int BodyLimb(Dam_EnemyDamageBase d)
    {
        try
        {
            var limbs = d.DamageLimbs;
            if (limbs != null)
                foreach (var l in limbs)
                    if (l != null && l.m_type == eLimbDamageType.Normal && !l.IsDestroyed) return l.m_limbID;
        }
        catch { }
        return 0;
    }

    // ---------- aiming (shared with TGL3) ----------

    /// <summary>Target velocity from the aim point's movement between frames (smoothed).</summary>
    private static void TrackAim(Turret t)
    {
        var det = t.Inst.m_detection;
        if (det == null || !det.HasTarget || !t.Inst.TryGetTargetAimPos(out var cur)) { t.HasLastAim = false; return; }
        float dt = Time.time - t.LastAimTime;
        if (t.HasLastAim && dt > 0.01f && dt < 0.5f && (cur - t.LastAim).sqrMagnitude < 25f)
            t.AimVel = Vector3.Lerp(t.AimVel, (cur - t.LastAim) / dt, 0.3f);
        else if (!t.HasLastAim || dt >= 0.5f) t.AimVel = Vector3.zero;
        t.LastAim = cur;
        t.LastAimTime = Time.time;
        t.HasLastAim = true;
    }

    /// <summary>Where the target will be when the projectile arrives.</summary>
    private static Vector3 Lead(Vector3 from, Vector3 aim, Vector3 vel, float speed, float g)
    {
        var target = aim;
        if (vel.sqrMagnitude <= 0.04f) return target;
        for (int i = 0; i < 2; i++)
        {
            var v0 = Ballistic(from, target, speed, g);
            float flat = new Vector3(v0.x, 0f, v0.z).magnitude;
            float tof = flat > 0.1f ? new Vector3(target.x - from.x, 0f, target.z - from.z).magnitude / flat : (target - from).magnitude / speed;
            target = aim + new Vector3(vel.x, 0f, vel.z) * Math.Min(tof, 1.5f);
        }
        return target;
    }

    /// <summary>Launch velocity to hit 'to' from 'from' with the given speed and gravity (low arc; out of reach — 45°).</summary>
    private static Vector3 Ballistic(Vector3 from, Vector3 to, float speed, float g)
    {
        var d = to - from;
        var flat = new Vector3(d.x, 0f, d.z);
        float x = flat.magnitude, y = d.y;
        if (g <= 0.01f || x < 0.05f) return d.normalized * speed;
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
        float angle = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI / 4f;
        return (flat / x * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
    }
}
