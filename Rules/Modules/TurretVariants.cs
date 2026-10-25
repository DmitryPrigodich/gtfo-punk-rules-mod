using GameData;
using Gear;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// New sentries in the tool slot, made by cloning the stock sentry in the game tables (so EVERY player needs the mod):
///  - TL1 searchlight — turns after enemies in its sector, the beam disturbs and wakes sleepers. Doesn't shoot.
///  - TF I / TF II fog repellers — an endless fog-free sphere (repeller / scaled-down turbine model); their hum wakes sleepers.
///  - TB1 bio-tracker — scans its sector and tags enemies. Doesn't shoot.
///  - TCF3 foam — lobs acidic foam that slows and staggers instead of bullets.
///  - TGL3 grenade launcher — bursts of contact grenades.
/// Non-shooting ones run on a battery (their ammo). Combat sentries get bonuses against tagged enemies.
///
/// Files: this one — registry, game patches, per-frame loop; .Config — settings; .Models — model copies and placement;
/// .Light / .Fog / .Tracker / .Foam / .Grenade — one sentry kind each; .Battery — battery and screen; .Diagnostics.
/// </summary>
public partial class TurretVariants : ModuleBase
{
    public override string Id => "TurretVariants";
    public override string Description => "New sentries: DRED GARAGE TL1 (searchlight), TF I / TF II (fog repellers), TB1 (bio-tracker), TCF3 (foam), TGL3 (grenades). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private enum Mode { Fog, Light, FogTurbine, Tracker, Foam, Grenade }

    /// <summary>Kinds that turn after enemies and scan normally (the others stand still).</summary>
    private static bool Tracks(Mode m) => m is Mode.Light or Mode.Tracker or Mode.Foam or Mode.Grenade;

    /// <summary>Kinds that hide the autocannon barrel even without tool parts in the cradle.</summary>
    private static bool HidesGun(Mode m) => m is Mode.Light or Mode.Fog or Mode.FogTurbine;
    private static bool HidesGun(Turret t) => HidesGun(t.Mode) || t.PartsAttached;

    private const uint SentryCategoryId = 12;

    /// <summary>A placed sentry of one of our kinds.</summary>
    private class Turret
    {
        public SentryGunInstance Inst;
        public Mode Mode;
        public Kind Kind;
        public bool Ready;
        public float GiveUpAt, SpawnedAt, NextHide;
        public GameObject Extra;                                     // destroyed with the sentry
        public Light Light, Glow, FogBulb;
        public FogRepeller_Sphere Sphere;
        public bool Flat;                                            // battery empty: switched off
        public bool PartsAttached;
        public TMPro.TextMeshPro Screen;
        public float NextDisturb, NextScan, NextShot, NextBurst;
        public int Tagged, BurstLeft, MagLeft = -1;
        public float LastShotAt;
        public Vector3 LastAim, AimVel;
        public float LastAimTime;
        public bool HasLastAim;
        public readonly List<Action> Reapply = new();                   // re-place models after a config edit
        public readonly Dictionary<IntPtr, float> ExposedSince = new(); // how long an enemy has been in the beam / hearing the hum
    }

    private static readonly Dictionary<IntPtr, Turret> _turrets = new();
    private static readonly Dictionary<uint, Mode> _modeByCategory = new();
    private static readonly List<IntPtr> _dead = new();

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _turrets.Clear(); _status.Clear(); _nextStagger.Clear(); ResetDiagnostics(); };
        GearFactory.OnInject += Inject;
    }

    // ---------- adding the sentries to the game tables ----------

    private static void Inject()
    {
        try { ApplyTagBonus(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] tagged-target bonus: {e.Message}"); }
        var baseGear = GearFactory.FindByCategory(SentryCategoryId, b => GearFactory.NameOf(b).IndexOf("Burst", StringComparison.OrdinalIgnoreCase) >= 0);
        if (baseGear == null) { ModInfo.Log.LogWarning("[TurretVariants] stock sentry not found in game data — new sentries not added"); return; }

        // Keys are fixed IDs in GearFactory (never change them: saved favorites refer to them).
        Add(baseGear, _fog, "GtfoPunk_FogTurret",
            Lang.T("Stationary fog repeller. Keeps a fog-free sphere around itself; its hum disturbs sleepers nearby. Does not shoot.",
                   "Стационарный отпугиватель тумана. Держит вокруг себя сферу без тумана, гул тревожит спящих рядом. Не стреляет."));
        Add(baseGear, _light, "GtfoPunk_LightTurret",
            Lang.T("Searchlight that tracks enemies in its sector. The beam wakes sleepers. Does not shoot.",
                   "Прожектор, который поворачивается за врагами в своём секторе. Луч будит спящих. Не стреляет."));
        Add(baseGear, _turbine, "GtfoPunk_FogTurbineTurret",
            Lang.T("Fog repeller with a miniature turbine. Keeps a fog-free sphere around itself; its hum disturbs sleepers nearby. Does not shoot.",
                   "Отпугиватель тумана с уменьшенной турбиной. Держит вокруг себя сферу без тумана, гул тревожит спящих рядом. Не стреляет."));
        // Categories of retired test variants stay as searchlight aliases, so old saved favorites still assemble.
        if (_light.Enabled.Value)
            foreach (var retired in new[] { "GtfoPunk_LightTurret_Retired4207", "GtfoPunk_LightTurret_Retired4208" })
                _modeByCategory[GearFactory.CloneCategory(SentryCategoryId, retired, _light.Name.Value, null)] = Mode.Light;
        Add(baseGear, _tracker, "GtfoPunk_TrackerTurret",
            Lang.T("Bio-tracker sentry. Scans its sector every few seconds and tags enemies, even through walls. Combat sentries handle tagged enemies better. Does not shoot.",
                   "Турель-биотрекер. Раз в несколько секунд сканирует свой сектор и метит врагов, даже за стенами. Боевые турели лучше работают по меченым. Не стреляет."));
        Add(baseGear, _foam, "GtfoPunk_FoamTurret",
            Lang.T("C-foam sentry. Lobs acidic foam globs: enemies slow down, take damage and lose their attacks. Uses sentry ammo.",
                   "Пенострел. Кидает кислотную пену: враги замедляются, получают урон и сбиваются с атаки. Тратит патроны турели."));
        Add(baseGear, _grenade, "GtfoPunk_GrenadeTurret",
            Lang.T("Grenade launcher sentry. Bursts of grenades that explode on contact. Won't fire close to itself or near players. Uses sentry ammo.",
                   "Гранатомётная турель. Очереди гранат со взрывом на контакт. Не стреляет вплотную и рядом с игроками. Тратит патроны турели."));
    }

    private static void Add(PlayerOfflineGearDataBlock baseGear, Kind kind, string key, string desc)
    {
        if (!kind.Enabled.Value) return;
        uint catId = GearFactory.CloneCategory(SentryCategoryId, key, kind.Name.Value, desc);
        GearFactory.AddGear(baseGear, key, kind.Name.Value, new Dictionary<int, uint?> { [(int)eGearComponent.Category] = catId });
        _modeByCategory[catId] = kind.Mode;
    }

    // ---------- game patches ----------

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnSpawn))]
    private static class Patch_Spawn
    {
        private static void Postfix(SentryGunInstance __instance, pGearSpawnData spawnData)
        {
            try { AddStatusLight(__instance); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] status light: {e.Message}"); }
            try
            {
                var gear = new GearIDRange(spawnData.gearIDRange);
                if (!_modeByCategory.TryGetValue(gear.GetCompID(eGearComponent.Category), out var mode)) return;
                var kind = KindOf(mode);
                _turrets[__instance.Pointer] = new Turret { Inst = __instance, Mode = mode, Kind = kind, GiveUpAt = Time.time + 10f, SpawnedAt = Time.time };
                ModInfo.Log.LogInfo($"[TurretVariants] sentry placed: {kind.Label.Value}");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] OnSpawn: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnDespawn))]
    private static class Patch_Despawn
    {
        private static void Prefix(SentryGunInstance __instance)
        {
            RemoveStatusLight(__instance);
            if (!_turrets.TryGetValue(__instance.Pointer, out var t)) return;
            try { t.Sphere?.KillRepellerInstantly(); } catch { }
            if (t.Extra != null) UnityEngine.Object.Destroy(t.Extra);
            _turrets.Remove(__instance.Pointer);
        }
    }

    private static Turret Of(SentryGunInstance_Firing_Bullets firing)
    {
        var core = firing?.m_core;
        var inst = core == null ? null : ((Il2CppObjectBase)core).TryCast<SentryGunInstance>();
        return inst != null && _turrets.TryGetValue(inst.Pointer, out var t) ? t : null;
    }

    // Our sentries never fire the stock bullets (foam and grenades are fired by us).
    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireMaster))]
    private static class Patch_FireMaster { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireClient))]
    private static class Patch_FireClient { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToFire))]
    private static class Patch_WantFire
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (!_turrets.ContainsKey(__instance.Pointer)) return true;
            __result = false;
            return false;
        }
    }

    // The fog repellers don't turn after enemies…
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.UpdateRotation))]
    private static class Patch_Rotation
    {
        private static bool Prefix(SentryGunInstance __instance) => !(_turrets.TryGetValue(__instance.Pointer, out var t) && !Tracks(t.Mode));
    }

    // …and don't scan (no scan beam); a switched-off sentry doesn't scan either.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToScan))]
    private static class Patch_Scan
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (!_turrets.TryGetValue(__instance.Pointer, out var t) || (Tracks(t.Mode) && !t.Flat)) return true;
            __result = false;
            return false;
        }
    }

    // ---------- per-frame loop ----------

    private static void Tick()
    {
        DiagnosticsTick();
        PrewarmTick();
        CheckReload();
        StatusTick();
        if (_turrets.Count == 0) return;
        try { FoamEffects(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] foam effects: {e.Message}"); }

        _dead.Clear();
        foreach (var kv in _turrets)
        {
            var t = kv.Value;
            if (t.Inst == null) { _dead.Add(kv.Key); continue; }
            try
            {
                if (!t.Ready) { SetUp(t); continue; }
                Battery(t);
                UpdateScreen(t);
                // Sentry parts may be created a bit later — keep hiding the barrel for a few seconds.
                if (HidesGun(t) && Time.time - t.SpawnedAt < 8f && Time.time >= t.NextHide) { t.NextHide = Time.time + 0.5f; HideGun(t.Inst); }
                if (t.Flat) continue;
                switch (t.Mode)
                {
                    case Mode.Light: LightTick(t); break;
                    case Mode.Fog: case Mode.FogTurbine: FogTick(t); break;
                    case Mode.Tracker: TrackerTick(t); break;
                    case Mode.Foam: if (SNetwork.SNet.IsMaster) FoamTick(t); break;
                    case Mode.Grenade: if (SNetwork.SNet.IsMaster) GrenadeTick(t); break;
                }
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] {t.Mode}: {e.Message}"); t.Ready = true; }
        }
        foreach (var k in _dead) _turrets.Remove(k);
    }

    /// <summary>Builds the sentry's look and behaviour once its parts exist (retried every frame for up to 10 s).</summary>
    private static void SetUp(Turret t)
    {
        t.Ready = t.Mode switch
        {
            Mode.Light => SetupLight(t),
            Mode.Tracker => SetupTracker(t),
            Mode.Foam => SetupFoam(t),
            Mode.Grenade => SetupGrenade(t),
            _ => SetupFog(t),
        };
        if (t.Ready) ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value} set up");
        else if (Time.time > t.GiveUpAt) { ModInfo.Log.LogWarning($"[TurretVariants] failed to set up {t.Kind.Label.Value}"); t.Ready = true; }
    }

    // ---------- shared helpers ----------

    /// <summary>The sentry owner as a noise maker. Il2Cpp: interfaces need Cast — TryCast returns null.</summary>
    private static INM_NoiseMaker NoiseMaker(Turret t)
    {
        try { var owner = t.Inst.Owner; return owner == null ? null : ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>(); }
        catch { return null; }
    }

    private static void MakeNoiseAt(INM_NoiseMaker maker, Enemies.EnemyAgent enemy, NM_NoiseType type) =>
        NoiseManager.MakeNoise(new NM_NoiseData
        {
            noiseMaker = maker,
            position = enemy.Position,
            node = enemy.CourseNode,
            radiusMin = 0.01f,
            radiusMax = 1f,
            yScale = 1f,
            type = type,
            includeToNeightbourAreas = false,
            raycastFirstNode = false,
        });

    /// <summary>Living enemies whose damage colliders are within the radius, each once.</summary>
    private static IEnumerable<Enemies.EnemyAgent> EnemiesNear(Vector3 pos, float radius)
    {
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(pos, radius, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var e = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (e != null && e.Alive && seen.Add(e.Pointer)) yield return e;
        }
    }

    /// <summary>
    /// Wake-up timer for enemies exposed to a beam / hum: disturb now, wake (InstaDetect) after wakeAfter seconds
    /// of continuous exposure. Enemies no longer exposed get their timer reset. Returns how many were woken.
    /// </summary>
    private static int Expose(Turret t, IEnumerable<Enemies.EnemyAgent> exposed, NM_NoiseType type, float wakeAfter)
    {
        var maker = NoiseMaker(t);
        if (maker == null) return 0;
        var seen = new HashSet<IntPtr>();
        int woke = 0;
        foreach (var e in exposed)
        {
            if (e.CourseNode == null || !seen.Add(e.Pointer)) continue;
            if (!t.ExposedSince.TryGetValue(e.Pointer, out var since)) t.ExposedSince[e.Pointer] = since = Time.time;
            bool wake = wakeAfter > 0f && Time.time - since >= wakeAfter;
            if (wake) woke++;
            MakeNoiseAt(maker, e, wake ? NM_NoiseType.InstaDetect : type);
        }
        foreach (var k in t.ExposedSince.Keys.Where(k => !seen.Contains(k)).ToList()) t.ExposedSince.Remove(k);
        return woke;
    }

    private static SentryGunInstance_Firing_Bullets Firing(Turret t) =>
        t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
}
