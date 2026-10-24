using System;
using System.Collections.Generic;
using System.Linq;
using Enemies;
using System.Reflection;
using System.Text.Json.Nodes;
using GameData;
using Gear;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Localization;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// New sentries in the tool slot (picked in the loadout like the regular sentry):
///  - FT1 anti-fog: doesn't shoot, keeps a fog-free sphere around itself (like a fog repeller, but endless).
///  - LT2 searchlight: placed facing one way, shines a spotlight and turns after enemies in its sector;
///    brighter when it sees an enemy. Doesn't shoot.
///  - BT3 bio-tracker sentry: doesn't shoot, scans its sector every few seconds and tags enemies (like a bio-tracker).
///  - CF4 foam sentry: fires foam globs (like the C-foam launcher) at the target instead of bullets, uses sentry ammo.
/// Made by cloning the stock sentry in the game tables, so EVERY player must have the mod.
/// </summary>
public class TurretVariants : ModuleBase
{
    public override string Id => "TurretVariants";
    public override string Description => "New tools: DRED GARAGE FT1 (anti-fog), LT2 (searchlight), BT3 (bio-tracker), CF4 (foam), GLF-3 (grenades). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static BepInEx.Configuration.ConfigEntry<bool> _fog, _light;
    private static BepInEx.Configuration.ConfigEntry<float> _fogRange, _lightRange, _lightAngle, _lightIntensity, _disturbInterval;
    private static BepInEx.Configuration.ConfigEntry<string> _disturb, _lampItem, _lampOffset, _lampRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _lampScale, _lightForward, _fogLamp, _fogModelScale;
    private static BepInEx.Configuration.ConfigEntry<bool> _hideGun, _turbine;
    private static BepInEx.Configuration.ConfigEntry<float> _turbineScale, _turbineRange;
    private static BepInEx.Configuration.ConfigEntry<string> _turbineOffset, _turbineKeywords;
    private static BepInEx.Configuration.ConfigEntry<string> _fogModelOffset, _fogModelRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _lampGlow, _lampGlowForward;
    private static BepInEx.Configuration.ConfigEntry<float> _lightWakeAfter;
    private static BepInEx.Configuration.ConfigEntry<bool> _liveReload;
    private static BepInEx.Configuration.ConfigEntry<string> _foamParts, _foamPartsOffset, _foamPartsRotation, _trackerParts, _trackerPartsOffset, _trackerPartsRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _foamPartsScale, _trackerPartsScale;
    private static BepInEx.Configuration.ConfigEntry<bool> _gl, _hideSensor;
    private static BepInEx.Configuration.ConfigEntry<bool> _battery;
    private static BepInEx.Configuration.ConfigEntry<float> _batteryLight, _batteryFog, _batteryTracker;
    private static BepInEx.Configuration.ConfigEntry<int> _glMag;
    private static BepInEx.Configuration.ConfigEntry<float> _glShotDelay, _glReload;
    private static BepInEx.Configuration.ConfigEntry<float> _glInterval, _glSpeed, _glRange, _glMinRange, _glAmmo, _glSafe, _glPartsScale;
    private static BepInEx.Configuration.ConfigEntry<string> _glParts, _glPartsOffset, _glPartsRotation;
    private static BepInEx.Configuration.ConfigEntry<int> _foamBurst;
    private static BepInEx.Configuration.ConfigEntry<float> _foamBurstDelay;
    private static BepInEx.Configuration.ConfigEntry<bool> _foamLead;
    private static BepInEx.Configuration.ConfigEntry<string> _foamModel, _foamModelOffset, _foamModelRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _foamModelScale;
    private static BepInEx.Configuration.ConfigEntry<bool> _tracker, _trackerWalls, _foam, _foamSkipGlued, _trackerSound;
    private static BepInEx.Configuration.ConfigEntry<float> _trackerRange, _trackerAngle, _trackerInterval;
    private static BepInEx.Configuration.ConfigEntry<float> _foamInterval, _foamSpeed, _foamGravity, _foamAmmo, _foamExpand, _foamRange;
    private static BepInEx.Configuration.ConfigEntry<float> _foamMaxGlue, _foamAcid, _foamStagger, _foamStaggerInterval;
    private static BepInEx.Configuration.ConfigEntry<string> _humNoise;
    private static BepInEx.Configuration.ConfigEntry<float> _humRadius, _humInterval, _humWakeAfter, _tagDamage, _tagTurn, _tagDelay;
    private static BepInEx.Configuration.ConfigEntry<bool> _tagPrio;

    protected override void OnBindConfig()
    {
        _fog = Bind("FogTurret", true, "Add the anti-fog sentry.");
        _fogRange = Bind("FogRange2", 10f, "Radius of FT1's fog-free sphere (m). 10 — like the real fog repeller (measured in 0.3.1).");
        _light = Bind("LightTurret", true, "Add the searchlight sentry.");
        _lightRange = Bind("LightRange", 30f, "Searchlight range (m).");
        _lightAngle = Bind("LightAngle", 45f, "Searchlight beam angle (degrees).");
        _lightIntensity = Bind("LightIntensity3", 1.1f, "Searchlight brightness (1.6x brighter when an enemy is in the sector).");
        _disturb = Bind("LightDisturbs2", "Detectable", "How the light affects enemies in the beam: Detectable — disturbs (may wake them, like footsteps nearby), PulseOnly — pulse only, Off — nothing.");
        _disturbInterval = Bind("LightDisturbInterval", 1.5f, "How often (s) the light disturbs enemies in the beam.");
        _lightWakeAfter = Bind("LightWakeAfter", 3f, "A sleeping enemy that stays in the beam this many seconds in a row wakes up. 0 — never wake.");
        _humNoise = Bind("FogHum", "Detectable", "FT1 / FT1-T hum heard by enemies nearby (through walls): Detectable — disturbs, PulseOnly — pulse only, Off — silent.");
        _humRadius = Bind("FogHumRadius", 6f, "How far the FT1 / FT1-T hum is heard (m).");
        _humInterval = Bind("FogHumInterval", 2f, "How often the hum disturbs enemies (s).");
        _humWakeAfter = Bind("FogHumWakeAfter", 8f, "A sleeper that hears the hum this many seconds in a row wakes up. 0 — never wake.");
        _tagDamage = Bind("TaggedDamageBonus", 1.3f, "Combat sentries vs tagged enemies (BT3, bio-tracker): damage and stagger multiplier on top of the game's own.");
        _tagTurn = Bind("TaggedTurnBonus", 1.5f, "Combat sentries vs tagged enemies: rotation speed multiplier (they lock on faster).");
        _tagDelay = Bind("TaggedStartDelay", 0.5f, "Combat sentries vs tagged enemies: multiplier of the delay before opening fire (less = quicker).");
        _tagPrio = Bind("TaggedPriority", true, "Combat sentries prefer tagged enemies.");
        _lampItem = Bind("LampModel", "Long Range Flashlight", "Item whose model is mounted on the searchlight sentry (name or part of it; empty — no model).");
        _lampOffset = Bind("LampOffset3", "0,-0.02,0.04", "Lamp model offset from the barrel root (x — sideways, y — up, z — forward, m).");
        _lampGlow = Bind("LampGlow", 1.5f, "Lamp lens glow (small light right in front of the glass), 0 — none.");
        _lampGlowForward = Bind("LampGlowForward", 0.2f, "How far in front of the lamp model the lens glow sits (m).");
        _liveReload = Bind("LiveReload", true, "Re-read this config on the fly while the game runs: edit model positions in the file, save — models on placed sentries move right away.");
        _lampRotation = Bind("LampRotation4", "90,270,0", "Lamp model rotation (degrees x,y,z). The first number is the roll around the lamp axis (90 / -90 — handle down or up, 0 — sideways).");
        _lampScale = Bind("LampScale", 1f, "Lamp model scale.");
        _lightForward = Bind("LightForward", 0.35f, "How far in front of the lamp model the light source sits (m), so it doesn't light up the lamp itself.");
        _hideGun = Bind("HideGun", true, "Hide the autocannon barrel on sentries that don't shoot bullets (the tripod and body stay).");
        _fogModelOffset = Bind("FogModelOffset2", "0,0.3,-0.05", "Where the repeller model sits on FT1 (x,y,z, m from the top of the sentry; z — forward).");
        _fogModelRotation = Bind("FogModelRotation", "0,0,0", "Extra rotation of the repeller model on FT1 (degrees x,y,z) if it stands crooked.");
        _fogModelScale = Bind("FogModelScale", 1f, "Repeller model scale on FT1.");
        _turbine = Bind("FogTurbineTurret", true, "Test sentry DRED GARAGE FT1-T: scaled-down big fog repeller turbine.");
        _turbineScale = Bind("TurbineScale", 0.3f, "Turbine model scale on FT1-T.");
        _turbineOffset = Bind("TurbineSeat3", "0.15,-0.6,-0.05", "Where the turbine sits on FT1-T: the bottom of the model goes into the barrel cradle, plus this offset (x — sideways, y — up, z — forward, m).");
        _turbineKeywords = Bind("TurbineModelSearch2", "defoggerbig", "If item 133 has no model — search loaded meshes whose name contains one of these words.");
        _turbineRange = Bind("TurbineRange2", 10f, "Radius of FT1-T's fog-free sphere (m). 10 — like the turbine (measured in 0.3.1).");
        _fogLamp = Bind("FogLamp", 0.5f, "Brightness of the dim bulb on FT1 (like a glow stick). 0 — no bulb.");
        _tracker = Bind("TrackerTurret", true, "Add the bio-tracker sentry DRED GARAGE BT3 (tags enemies in its sector, doesn't shoot).");
        _trackerRange = Bind("TrackerRange", 25f, "BT3 scan range (m).");
        _trackerAngle = Bind("TrackerAngle", 90f, "BT3 sector width (degrees, horizontal, from the placement direction).");
        _trackerInterval = Bind("TrackerInterval", 3f, "How often BT3 scans its sector (s).");
        _trackerSound = Bind("TrackerSound", true, "BT3 beeps like a bio-tracker when enemies are in its sector.");
        _trackerWalls = Bind("TrackerThroughWalls", true, "BT3 sees through walls (like the handheld bio-tracker). false — line of sight only.");
        _foam = Bind("FoamTurret", true, "Add the foam sentry DRED GARAGE CF4 (shoots foam instead of bullets).");
        _foamInterval = Bind("FoamInterval2", 1.5f, "Pause between foam bursts (s).");
        _foamBurst = Bind("FoamBurst", 3, "Globs per burst.");
        _foamBurstDelay = Bind("FoamBurstDelay", 0.12f, "Pause between globs in a burst (s).");
        _foamSpeed = Bind("FoamSpeed2", 30f, "Initial foam glob speed (m/s).");
        _foamGravity = Bind("FoamGravity2", 9f, "Glob gravity for the trajectory (m/s²; 9 in the game).");
        _foamLead = Bind("FoamLead", true, "Lead moving targets (by target velocity).");
        _foamAmmo = Bind("FoamAmmoPerShot3", 2f, "How many sentry 'bullets' one foam glob costs (sentry reserve is 150/0.58 ≈ 257 bullets).");
        _foamModel = Bind("FoamModel", "C-Foam Grenade", "Model of the foam canisters on CF4 (item name; empty — none).");
        _foamModelOffset = Bind("FoamModelOffset", "0.09,0.02,0.05", "Where the canisters hang on CF4 (x — sideways, mirrored on both sides; y — up; z — forward, m from the barrel root).");
        _foamModelRotation = Bind("FoamModelRotation", "90,0,0", "Canister rotation (degrees x,y,z).");
        _foamModelScale = Bind("FoamModelScale", 1f, "Canister scale.");
        _foamParts = Bind("FoamGunParts2", "33=4/4", "C-foam launcher parts mounted in CF4's cradle instead of the barrel (type=ID/subtype from GearList: 33 — Delivery, subtype from 32; 37 — Payload, subtype from 36). Empty — keep the autocannon barrel.");
        _foamPartsOffset = Bind("FoamGunOffset2", "0,0,0.1", "Offset of the foam launcher parts in the cradle (x — sideways, y — up, z — forward, m).");
        _foamPartsRotation = Bind("FoamGunRotation", "0,0,0", "Rotation of the foam launcher parts (degrees x,y,z).");
        _foamPartsScale = Bind("FoamGunScale", 1f, "Scale of the foam launcher parts.");
        _trackerParts = Bind("TrackerParts", "33=3", "Bio-tracker parts in BT3's cradle (33 — Delivery_Bio_Tracker; 42=3 — screen can be added). Empty — keep the barrel.");
        _trackerPartsOffset = Bind("TrackerPartsOffset", "0,0,0", "Offset of the bio-tracker parts in the cradle (m).");
        _trackerPartsRotation = Bind("TrackerPartsRotation", "0,0,0", "Rotation of the bio-tracker parts (degrees).");
        _trackerPartsScale = Bind("TrackerPartsScale", 1f, "Scale of the bio-tracker parts.");
        _hideSensor = Bind("HideSensor", false, "Hide the targeting sensor part on sentries with a hidden barrel — it floats in the air. The scan beam itself stays.");
        _gl = Bind("GrenadeTurret", true, "Add the grenade launcher sentry DRED GARAGE GLF-3 (fires grenades that explode on contact; needs the Grenades module).");
        _glInterval = Bind("GrenadeInterval", 2.5f, "Unused since 0.5.3 (see GrenadeMagazine / GrenadeShotDelay / GrenadeReload).");
        _battery = Bind("Battery", true, "Non-shooting sentries (LT2, FT1, FT1-T, BT3) run on their ammo as a battery: it drains while they work, at zero they switch off. Recharge like a normal sentry: pick it up, use a tool refill, place it again.");
        _batteryLight = Bind("BatterySecondsLT2", 300f, "How long a full battery lasts on LT2 (s).");
        _batteryFog = Bind("BatterySecondsFT1", 300f, "How long a full battery lasts on FT1 / FT1-T (s).");
        _batteryTracker = Bind("BatterySecondsBT3", 300f, "How long a full battery lasts on BT3 (s).");
        _glMag = Bind("GrenadeMagazine2", 3, "Grenades per burst, fired in quick succession before a reload.");
        _glShotDelay = Bind("GrenadeShotDelay", 0.35f, "Pause between grenades within the drum (s).");
        _glReload = Bind("GrenadeReload2", 2.5f, "Reload time after a burst (s). A partly used burst also reloads after this long without a target.");
        _glSpeed = Bind("GrenadeSpeed", 16f, "Grenade launch speed (m/s).");
        _glRange = Bind("GrenadeRange", 25f, "Doesn't fire beyond this distance (m).");
        _glMinRange = Bind("GrenadeMinRange", 4f, "Doesn't fire closer than this (m) — so it doesn't blow itself up.");
        _glSafe = Bind("GrenadePlayerSafe", 4f, "Don't fire if a player stands within this many meters of the impact point.");
        _glAmmo = Bind("GrenadeAmmoPerShot", 12f, "How many sentry 'bullets' one grenade costs.");
        _glParts = Bind("GrenadeParts", "33=2,37=1", "Mine deployer parts in GLF-3's cradle (33 — Delivery_Mine_Deployer, 37 — Payload_Mine_1). Empty — autocannon barrel.");
        _glPartsOffset = Bind("GrenadePartsOffset", "0,0,0", "Offset of the mine deployer parts in the cradle (m).");
        _glPartsRotation = Bind("GrenadePartsRotation", "0,0,0", "Rotation of the mine deployer parts (degrees).");
        _glPartsScale = Bind("GrenadePartsScale2", 0.8f, "Scale of the mine deployer parts.");
        _fogDump = Bind("FogDiagnostics", false, "Write fog repeller measurements to GtfoPunk.FogRepellers.txt a few seconds into each level (diagnostics; causes a hitch).");
        _foamExpand = Bind("FoamExpand", -1f, "Glob expansion volume (-1 — the foam launcher's default).");
        _foamRange = Bind("FoamRange", 20f, "The foam sentry doesn't fire beyond this distance (m).");
        _foamMaxGlue = Bind("FoamMaxGlue", 0.75f, "CF4 foam slows enemies instead of freezing them: glue is kept below this share of 'stuck' (1 — freeze like the C-foam launcher). Applies near a CF4.");
        _foamAcid = Bind("FoamAcidDps", 3f, "CF4 foam is acidic: damage per second to enemies with foam on them (0 — off).");
        _foamStagger = Bind("FoamStagger", 20f, "CF4 hits stagger enemies (interrupting attacks): stagger multiplier of the hit (0 — off).");
        _foamStaggerInterval = Bind("FoamStaggerInterval", 2f, "How often one enemy can be staggered by CF4 foam (s).");
        _foamSkipGlued = Bind("FoamSkipGlued", true, "Don't waste foam on an enemy that is already glued and immobilized.");
    }

    private enum Mode { Fog, Light, FogTurbine, Tracker, Foam, Grenade }

    /// <summary>Modes that turn after enemies and scan normally (the others stand still).</summary>
    private static bool Tracks(Mode m) => m == Mode.Light || m == Mode.Tracker || m == Mode.Foam || m == Mode.Grenade;
    /// <summary>Modes that hide the autocannon barrel.</summary>
    private static bool HidesGun(Mode m) => m == Mode.Light || m == Mode.Fog || m == Mode.FogTurbine;

    /// <summary>This sentry has tool parts instead of the barrel — hide the barrel.</summary>
    private static bool HidesGun(Turret t) => HidesGun(t.Mode) || t.PartsAttached;
    private const uint FogTurbineItemId = 133; // Carry_HeavyFogRepeller — the big mission turbine

    private const uint SentryCategoryId = 12;
    private const uint FogRepellerItemId = 117; // CONSUMABLE_FogRepeller

    private static readonly Dictionary<uint, (Mode Mode, int Variant)> _modeByCategory = new();

    private class Turret
    {
        public SentryGunInstance Inst;
        public Mode Mode;
        public bool Ready;
        public float GiveUpAt;
        public GameObject Extra;
        public Light Light, Glow, FogBulb;
        public bool Flat;          // battery empty: the sentry is switched off
        public FogRepeller_Sphere Sphere;
        public float NextDisturb, SpawnedAt, NextHide;
        public TMPro.TextMeshPro Screen;
        public float NextScan, NextShot, NextBurst;
        public int Tagged, Variant, BurstLeft;
        public bool PartsAttached;
        public int MagLeft = -1;
        public float LastShotAt;
        public Vector3 LastAim, AimVel;
        public float LastAimTime;
        public bool HasLastAim;
        public readonly List<Action> Reapply = new();               // re-place models after a config edit
        public readonly Dictionary<IntPtr, float> LitSince = new();  // how long an enemy has been in the beam (LT2)
    }

    private static readonly Dictionary<IntPtr, Turret> _turrets = new();

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _turrets.Clear(); _nextStagger.Clear(); _fogDumpAt = -1f; _fogDumped = false; };
        GearFactory.OnInject += Inject;
    }

    // ---------- adding tools to the game tables ----------

    private static void Inject()
    {
        try { ApplyTagBonus(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] tagged-target bonus: {e.Message}"); }
        var baseGear = GearFactory.FindByCategory(SentryCategoryId, b => GearFactory.NameOf(b).IndexOf("Burst", StringComparison.OrdinalIgnoreCase) >= 0);
        if (baseGear == null) { ModInfo.Log.LogWarning("[TurretVariants] stock sentry not found in game data — new sentries not added"); return; }
        if (_fog.Value) AddVariant(baseGear, Mode.Fog, "GtfoPunk_FogTurret", "DRED GARAGE FT1",
            Lang.T("Stationary fog repeller. Keeps a fog-free sphere around itself. Does not shoot.", "Стационарный отпугиватель тумана. Держит вокруг себя сферу без тумана. Не стреляет."));
        if (_light.Value) AddVariant(baseGear, Mode.Light, "GtfoPunk_LightTurret", "DRED GARAGE LT2",
            Lang.T("Searchlight that tracks enemies in its sector. Does not shoot.", "Прожектор, который поворачивается за врагами в своём секторе. Не стреляет."));
        // Test: scaled-down copy of the big fog repeller turbine. Added last so the other IDs don't shift.
        if (_turbine.Value) AddVariant(baseGear, Mode.FogTurbine, "GtfoPunk_FogTurbineTurret", "DRED GARAGE FT1-T",
            Lang.T("TEST: miniature fog repeller turbine. Smaller fog-free sphere. Does not shoot.", "ТЕСТ: уменьшенная турбина-отпугиватель. Сфера без тумана поменьше. Не стреляет."));
        // Categories of retired test LT2 variants stay as searchlight aliases, so old saved favorites still assemble.
        if (_light.Value)
            foreach (var retired in new[] { "GtfoPunk_LightTurret_Retired4207", "GtfoPunk_LightTurret_Retired4208" })
                _modeByCategory[GearFactory.CloneCategory(SentryCategoryId, retired, "DRED GARAGE LT2", null)] = (Mode.Light, 0);
        // IDs are fixed per key in GearFactory, so the order here no longer matters.
        // (order: BT3, CF4, GL5; test LT2-A/B/C from 0.4.2 removed — they were last, no IDs shifted)
        if (_tracker.Value) AddVariant(baseGear, Mode.Tracker, "GtfoPunk_TrackerTurret", "DRED GARAGE BT3",
            Lang.T("Bio-tracker turret. Scans its sector every few seconds and tags enemies, even through walls. Does not shoot.",
                   "Турель-биотрекер. Раз в несколько секунд сканирует свой сектор и метит врагов, даже за стенами. Не стреляет."));
        if (_foam.Value) AddVariant(baseGear, Mode.Foam, "GtfoPunk_FoamTurret", "DRED GARAGE CF4",
            Lang.T("C-foam sentry. Lobs foam globs at enemies instead of bullets, gluing them in place. Uses sentry ammo.",
                   "Пенострел. Вместо пуль закидывает врагов сгустками пены и приклеивает их. Тратит патроны турели."));
        if (_gl.Value) AddVariant(baseGear, Mode.Grenade, "GtfoPunk_GrenadeTurret", "DRED GARAGE GLF-3",
            Lang.T("Grenade launcher sentry. Fires bursts of 3 grenades that explode on contact (smaller blast than a mine). Won't fire close to itself or near players. Uses sentry ammo.",
                   "Гранатомётная турель. Очередями по 3 гранаты со взрывом на контакт (слабее мины). Не стреляет вплотную и рядом с игроками. Тратит патроны турели."));
    }

    private static void AddVariant(PlayerOfflineGearDataBlock baseGear, Mode mode, string key, string title, string desc, int variant = 0)
    {
        uint catId = GearFactory.CloneCategory(SentryCategoryId, key, title, desc);
        GearFactory.AddGear(baseGear, key, title, new Dictionary<int, uint?> { [(int)eGearComponent.Category] = catId });
        _modeByCategory[catId] = (mode, variant);
    }

    // ---------- behaviour of a placed sentry ----------

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnSpawn))]
    private static class Patch_Spawn
    {
        private static void Postfix(SentryGunInstance __instance, pGearSpawnData spawnData)
        {
            try
            {
                var gear = new GearIDRange(spawnData.gearIDRange);
                if (!_modeByCategory.TryGetValue(gear.GetCompID(eGearComponent.Category), out var mv)) return;
                var mode = mv.Mode;
                _turrets[__instance.Pointer] = new Turret { Inst = __instance, Mode = mode, Variant = mv.Variant, GiveUpAt = Time.time + 10f, SpawnedAt = Time.time };
                ModInfo.Log.LogInfo($"[TurretVariants] sentry placed: {mode}");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] OnSpawn: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnDespawn))]
    private static class Patch_Despawn
    {
        private static void Prefix(SentryGunInstance __instance)
        {
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

    // Our sentries don't shoot bullets.
    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireMaster))]
    private static class Patch_FireMaster { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireClient))]
    private static class Patch_FireClient { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    // The anti-fog one doesn't turn after enemies.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.UpdateRotation))]
    private static class Patch_Rotation
    {
        private static bool Prefix(SentryGunInstance __instance) =>
            !(_turrets.TryGetValue(__instance.Pointer, out var t) && !Tracks(t.Mode));
    }

    // FT1 doesn't look for anything: no scan beam and no turning.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToScan))]
    private static class Patch_Scan
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (_turrets.TryGetValue(__instance.Pointer, out var t) && (!Tracks(t.Mode) || t.Flat)) { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToFire))]
    private static class Patch_WantFire
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (_turrets.ContainsKey(__instance.Pointer)) { __result = false; return false; }
            return true;
        }
    }

    /// <summary>Hides the autocannon barrel and receiver (rendering only — attachment points stay).</summary>
    private static void HideGun(SentryGunInstance inst)
    {
        if (!_hideGun.Value) return;
        int hidden = 0;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            for (var tr = r.transform; tr != null && tr != inst.transform; tr = tr.parent)
            {
                string n = tr.name;
                if (n.StartsWith("GtfoPunk_")) break;
                if (n.StartsWith("Front_") || n.StartsWith("Receiver_") || (_hideSensor.Value && n.StartsWith("Targeting"))) { if (r.enabled) hidden++; r.enabled = false; break; }
            }
        }
        if (hidden > 0) ModInfo.Log.LogInfo($"[TurretVariants] barrel parts hidden: {hidden}");
    }

    private static readonly List<IntPtr> _dead = new();

    private static BepInEx.Configuration.ConfigEntry<bool> _fogDump;
    private static float _fogDumpAt = -1f;
    private static bool _fogDumped;

    // ---------- live config ----------

    private static System.IO.FileSystemWatcher _watcher;
    private static volatile bool _cfgChanged;
    private static float _reloadAt;

    private static void CheckReload()
    {
        if (!_liveReload.Value) return;
        if (_watcher == null)
        {
            try
            {
                var path = _liveReload.ConfigFile.ConfigFilePath;
                _watcher = new System.IO.FileSystemWatcher(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path))
                { NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size };
                _watcher.Changed += (_, _) => _cfgChanged = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] config watcher: {e.Message}"); _liveReload.Value = false; return; }
        }
        if (_cfgChanged) { _cfgChanged = false; _reloadAt = Time.unscaledTime + 0.3f; } // wait for the editor to finish writing
        if (_reloadAt > 0f && Time.unscaledTime >= _reloadAt)
        {
            _reloadAt = 0f;
            try
            {
                _liveReload.ConfigFile.Reload();
                int n = 0;
                foreach (var t in _turrets.Values) foreach (var a in t.Reapply) { try { a(); n++; } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] re-placing: {e.Message}"); } }
                ModInfo.Log.LogInfo($"[TurretVariants] config re-read, models re-placed: {n}");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] re-reading config: {e.Message}"); }
        }
    }

    private static void Tick()
    {
        // Once per level, 5 s after drop-in: real fog repeller parameters — to the log and a file.
        if (_fogDump.Value && !_fogDumped && GameUtil.InLevel)
        {
            if (_fogDumpAt < 0f) _fogDumpAt = Time.time + 5f;
            else if (Time.time >= _fogDumpAt) { _fogDumped = true; try { DumpFogRepellers(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] fog measurement: {e.Message}"); } }
        }
        CheckReload();
        if (_turrets.Count == 0) return;
        try { FoamEffects(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] foam effects: {e.Message}"); }
        _dead.Clear();
        foreach (var kv in _turrets)
        {
            var t = kv.Value;
            if (t.Inst == null) { _dead.Add(kv.Key); continue; }
            try
            {
                if (!t.Ready)
                {
                    t.Ready = t.Mode switch { Mode.Light => SetupLight(t), Mode.Tracker => SetupTracker(t), Mode.Foam => SetupFoam(t), Mode.Grenade => SetupGrenade(t), _ => SetupFog(t) };
                    if (!t.Ready && Time.time > t.GiveUpAt) { ModInfo.Log.LogWarning($"[TurretVariants] failed to set up sentry ({t.Mode})"); t.Ready = true; }
                    else if (t.Ready) ModInfo.Log.LogInfo($"[TurretVariants] sentry {t.Mode} set up");
                }
                else
                {
                    Battery(t);
                    UpdateScreen(t);
                    // Sentry parts may be created later — keep "hiding the barrel" for a few seconds.
                    if (HidesGun(t) && Time.time - t.SpawnedAt < 8f && Time.time >= t.NextHide) { t.NextHide = Time.time + 0.5f; HideGun(t.Inst); }
                    if (t.Mode == Mode.Tracker && !t.Flat && Time.time >= t.NextScan)
                    {
                        t.NextScan = Time.time + Math.Max(0.5f, _trackerInterval.Value);
                        TrackerScan(t);
                    }
                    if (t.Mode == Mode.Foam && SNetwork.SNet.IsMaster) FoamTick(t);
                    if (t.Mode == Mode.Grenade && SNetwork.SNet.IsMaster) GrenadeTick(t);
                    if ((t.Mode == Mode.Fog || t.Mode == Mode.FogTurbine) && !t.Flat && SNetwork.SNet.IsMaster && Time.time >= t.NextDisturb)
                    {
                        t.NextDisturb = Time.time + Math.Max(0.3f, _humInterval.Value);
                        Hum(t);
                    }
                    if (t.Mode == Mode.Light && t.Light != null && !t.Flat)
                    {
                        bool target = t.Inst.m_detection != null && t.Inst.m_detection.HasTarget;
                        t.Light.intensity = _lightIntensity.Value * (target ? 1.6f : 1f);
                        if (SNetwork.SNet.IsMaster && Time.time >= t.NextDisturb)
                        {
                            t.NextDisturb = Time.time + Math.Max(0.3f, _disturbInterval.Value);
                            Disturb(t);
                        }
                    }
                }
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] {e.Message}"); t.Ready = true; }
        }
        foreach (var k in _dead) _turrets.Remove(k);
    }

    private static bool SetupLight(Turret t)
    {
        var head = MakeHead(t, out var root, out var align);
        if (head == null) return false;

        var go = new GameObject("GtfoPunk_Searchlight");
        go.transform.SetParent(head.transform, false);
        go.transform.localRotation = Quaternion.identity;
        GameObject lamp = null;
        try { lamp = AttachLamp(head.transform); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] lamp model: {e.Message}"); }
        HideGun(t.Inst);
        ModInfo.Log.LogInfo($"[TurretVariants] lamp attached to '{root.name}', {Vector3.Distance(root.position, align.position):0.00} m from the muzzle point");
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = _lightRange.Value;
        light.spotAngle = _lightAngle.Value;
        light.intensity = _lightIntensity.Value;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.None;
        CL_Light.Create(eClusteredLightType.Spot, go, light); // GTFO renders lights through its own clustered system

        GameObject glow = null;
        if (_lampGlow.Value > 0f)
        {
            glow = new GameObject("GtfoPunk_LampGlow");
            glow.transform.SetParent(head.transform, false);
            var gl = glow.AddComponent<Light>();
            t.Glow = gl;
            gl.type = LightType.Point;
            gl.range = 0.5f;
            gl.intensity = _lampGlow.Value;
            gl.color = new Color(1f, 0.95f, 0.85f);
            gl.shadows = LightShadows.None;
            CL_Light.Create(eClusteredLightType.Point, glow, gl);
        }

        // Lamp and light position come from the config; re-placed when the config is edited on the fly.
        void Place()
        {
            var off = Vec(_lampOffset.Value, Vector3.zero);
            if (lamp != null)
            {
                lamp.transform.localPosition = off;
                lamp.transform.localRotation = Quaternion.Euler(Vec(_lampRotation.Value, Vector3.zero));
                lamp.transform.localScale = Vector3.one * _lampScale.Value;
            }
            go.transform.localPosition = off + Vector3.forward * _lightForward.Value; // light in front of the lens, not inside the model
            light.range = _lightRange.Value;
            light.spotAngle = _lightAngle.Value;
            if (glow != null)
            {
                glow.transform.localPosition = off + Vector3.forward * _lampGlowForward.Value;
                var gl = glow.GetComponent<Light>();
                if (gl != null) gl.intensity = _lampGlow.Value;
            }
        }
        Place();
        t.Reapply.Add(Place);

        t.Extra = go;
        t.Light = light;
        return true;
    }

    /// <summary>
    /// A point at the barrel root that rotates with the sentry head; direction — same as the barrel.
    /// MuzzleAlign is at the barrel tip, far in front; take the topmost ancestor named Front_* (the barrel part root).
    /// </summary>
    private static GameObject MakeHead(Turret t, out Transform root, out Transform align)
    {
        root = null;
        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        align = firing?.MuzzleAlign;
        if (align == null) return null;
        root = align.parent ?? align;
        for (var tr = align.parent; tr != null && tr != t.Inst.transform; tr = tr.parent)
            if (tr.name.StartsWith("Front_")) root = tr;
        var head = new GameObject("GtfoPunk_Head");
        head.transform.SetParent(root, false);
        head.transform.position = root.position;
        head.transform.rotation = align.rotation;
        return head;
    }

    /// <summary>Moves the model so the bottom of its bounds is at the seat point and its center is above it.</summary>
    private static void SeatModel(GameObject model, Vector3 seat)
    {
        Bounds b = default; bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        var bottomCenter = new Vector3(b.center.x, b.min.y, b.center.z);
        model.transform.position += seat - bottomCenter;
        ModInfo.Log.LogInfo($"[TurretVariants] FT1-T: turbine {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} m, seated in the cradle (moved {(seat - bottomCenter).magnitude:0.00} m)");
    }

    /// <summary>Model length along the z axis of the given node (from renderer bounds).</summary>
    private static float LengthAlongZ(GameObject model, Transform space)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float z = space.InverseTransformPoint(c).z;
                min = Math.Min(min, z); max = Math.Max(max, z);
            }
        }
        return max > min ? max - min : 0f;
    }

    private static bool SetupFoam(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f; // no barrel — just shoot without canisters
        AttachParts(t, head.transform, _foamParts, _foamPartsOffset, _foamPartsRotation, _foamPartsScale, "CF4");
        string item = _foamModel.Value;
        if (!string.IsNullOrWhiteSpace(item))
        {
            uint id = FindItemId(item);
            try
            {
                var rgo = AttachModel(head.transform, id, Vector3.zero, Vector3.zero, 1f, "GtfoPunk_FoamTankR");
                var lgo = AttachModel(head.transform, id, Vector3.zero, Vector3.zero, 1f, "GtfoPunk_FoamTankL");
                void Place()
                {
                    var off = Vec(_foamModelOffset.Value, Vector3.zero);
                    var rot = Quaternion.Euler(Vec(_foamModelRotation.Value, Vector3.zero));
                    if (rgo != null) { rgo.transform.localPosition = off; rgo.transform.localRotation = rot; rgo.transform.localScale = Vector3.one * _foamModelScale.Value; }
                    if (lgo != null)
                    {
                        lgo.SetActive(Math.Abs(off.x) > 0.001f);
                        lgo.transform.localPosition = new Vector3(-off.x, off.y, off.z); lgo.transform.localRotation = rot; lgo.transform.localScale = Vector3.one * _foamModelScale.Value;
                    }
                }
                Place();
                t.Reapply.Add(Place);
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] CF4 canisters: {e.Message}"); }
        }
        return true;
    }

    private static bool SetupTracker(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f;
        AttachParts(t, head.transform, _trackerParts, _trackerPartsOffset, _trackerPartsRotation, _trackerPartsScale, "BT3");
        return true;
    }

    /// <summary>Mounts copies of tool parts (from 'type=ID,type=ID') in the barrel cradle, if their models are loaded.</summary>
    private static void AttachParts(Turret t, Transform head,
        BepInEx.Configuration.ConfigEntry<string> parts, BepInEx.Configuration.ConfigEntry<string> offset,
        BepInEx.Configuration.ConfigEntry<string> rotation, BepInEx.Configuration.ConfigEntry<float> scale, string tag)
    {
        if (string.IsNullOrWhiteSpace(parts.Value)) return;
        var holder = new GameObject("GtfoPunk_Parts");
        holder.transform.SetParent(head, false);
        int n = 0;
        foreach (var kv in parts.Value.Split(','))
        {
            var p = kv.Split('=');
            if (p.Length != 2 || !int.TryParse(p[0].Trim(), out int type)) continue;
            var idSub = p[1].Split('/');
            if (!uint.TryParse(idSub[0].Trim(), out uint id)) continue;
            int sub = idSub.Length > 1 && int.TryParse(idSub[1].Trim(), out var ss) ? ss : -1;
            var prefab = FindPartPrefab(type, id, sub, out string name);
            if (prefab == null) { ModInfo.Log.LogWarning($"[TurretVariants] {tag}: model of part {type}={id} ({name ?? "?"}) not loaded"); continue; }
            if (CopyModel(prefab, holder.transform, "GtfoPunk_Part_" + type) != null) n++;
        }
        if (n == 0) { UnityEngine.Object.Destroy(holder); return; }
        t.PartsAttached = true;
        HideGun(t.Inst);
        void Place()
        {
            holder.transform.localPosition = Vec(offset.Value, Vector3.zero);
            holder.transform.localRotation = Quaternion.Euler(Vec(rotation.Value, Vector3.zero));
            holder.transform.localScale = Vector3.one * scale.Value;
        }
        Place();
        t.Reapply.Add(Place);
        ModInfo.Log.LogInfo($"[TurretVariants] {tag}: parts mounted in the cradle: {n}");
    }

    private static readonly Dictionary<string, GameObject> _partPrefabs = new();

    /// <summary>Gear part prefab by type and ID: model file name from the part table → loaded object with that name.</summary>
    /// <summary>Parts with subtypes (Delivery/Payload/Targeting) have a model per subtype: take the requested one or the first with a model.</summary>
    private static GearPartGeneralData Pick<T>(Il2CppSystem.Collections.Generic.List<T> list, Func<T, (int type, GearPartGeneralData data)> get, int sub)
    {
        if (list == null) return null;
        GearPartGeneralData first = null;
        foreach (var item in list)
        {
            var (type, data) = get(item);
            if (data == null || string.IsNullOrEmpty(data.Model)) continue;
            if (sub >= 0 && type == sub) return data;
            first ??= data;
        }
        return first;
    }

    private static GameObject FindPartPrefab(int type, uint id, int sub, out string name)
    {
        name = null;
        GearPartGeneralData general = type switch
        {
            12 => GameDataBlockBase<GearFrontPartDataBlock>.GetBlock(id)?.General,
            16 => GameDataBlockBase<GearReceiverPartDataBlock>.GetBlock(id)?.General,
            27 => GameDataBlockBase<GearToolMainPartDataBlock>.GetBlock(id)?.General,
            30 => GameDataBlockBase<GearToolGripPartDataBlock>.GetBlock(id)?.General,
            33 => Pick(GameDataBlockBase<GearToolDeliveryPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            37 => Pick(GameDataBlockBase<GearToolPayloadPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            40 => Pick(GameDataBlockBase<GearToolTargetingPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            42 => GameDataBlockBase<GearToolScreenPartDataBlock>.GetBlock(id)?.General,
            _ => null,
        };
        string model = general?.Model;
        if (string.IsNullOrEmpty(model)) return null;
        name = System.IO.Path.GetFileNameWithoutExtension(model);
        if (_partPrefabs.TryGetValue(name, out var cached) && cached != null) return cached;
        GameObject best = null;
        var arr = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<GameObject>());
        if (arr != null)
            foreach (var o in arr)
            {
                var g = o?.TryCast<GameObject>();
                if (g == null || g.name != name) continue;
                if (g.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                best = g;
                if (!g.scene.IsValid()) break; // a prefab (not a scene copy) — best
            }
        if (best != null) _partPrefabs[name] = best;
        return best;
    }

    private static readonly Dictionary<string, uint> _itemIds = new();

    private static uint FindItemId(string name)
    {
        if (_itemIds.TryGetValue(name, out var cached)) return cached;
        uint found = 0;
        string want = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (want.Length > 0)
            foreach (var b in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
            {
                if (b == null) continue;
                foreach (var n in new[] { b.publicName, b.name })
                {
                    string nn = new string((n ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                    if (nn.Length > 0 && nn.Contains(want)) { found = b.persistentID; break; }
                }
                if (found != 0) break;
            }
        if (found == 0) ModInfo.Log.LogWarning($"[TurretVariants] item '{name}' not found");
        _itemIds[name] = found;
        return found;
    }

    private static bool SetupFog(Turret t)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null || perMode.Length <= (int)ItemMode.Instance) return false;
        var dict = perMode[(int)ItemMode.Instance];
        if (dict == null || !dict.ContainsKey(FogRepellerItemId)) return false;
        var list = dict[FogRepellerItemId];
        if (list == null || list.Count == 0 || list[0] == null) return false;

        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        // Wait until the sentry assembles its parts (otherwise nothing to hide and nowhere to attach).
        if (firing?.MuzzleAlign == null && Time.time - t.SpawnedAt < 4f) return false;
        var top = firing?.MuzzleAlign != null ? firing.MuzzleAlign.position : t.Inst.transform.position + Vector3.up * 0.6f;
        var go = UnityEngine.Object.Instantiate(list[0], top, Quaternion.identity);
        go.transform.SetParent(t.Inst.transform, true);
        go.name = "GtfoPunk_FogSphere";
        HideGun(t.Inst);
        // Repeller model on top (mesh copy) and a dim bulb.
        try
        {
            var anchor = new GameObject("GtfoPunk_FogTop");
            anchor.transform.SetParent(t.Inst.transform, false);
            anchor.transform.position = top;
            // Exactly vertical even if the sentry stands on a slope; rotation — only the sentry's heading.
            anchor.transform.rotation = Quaternion.Euler(0f, t.Inst.transform.eulerAngles.y, 0f);
            GameObject turbine = null;
            if (t.Mode == Mode.FogTurbine)
            {
                turbine = AttachModel(anchor.transform, FogTurbineItemId, Vector3.zero, Vector3.zero, _turbineScale.Value, "GtfoPunk_FogModel", AnyMode);
                if (turbine != null)
                {
                    // The barrel cradle is the root of the Front_* part (barrel hidden). Put the turbine's bottom there, centered.
                    var head = MakeHead(t, out var cradle, out _);
                    if (head != null) UnityEngine.Object.Destroy(head);
                    var cradlePos = cradle != null ? cradle.position : top;
                    var tb = turbine; var an = anchor.transform;
                    void Seat()
                    {
                        tb.transform.localPosition = Vector3.zero;
                        tb.transform.localScale = Vector3.one * _turbineScale.Value;
                        SeatModel(tb, cradlePos + an.rotation * Vec(_turbineOffset.Value, Vector3.zero));
                    }
                    Seat();
                    t.Reapply.Add(Seat);
                }
                else ModInfo.Log.LogInfo("[TurretVariants] FT1-T: no turbine on this level — using the regular repeller");
            }
            if (turbine == null)
            {
                // The thrown (Instance) repeller stands upright; the pickup model lies tilted.
                var rep = AttachModel(anchor.transform, FogRepellerItemId, Vec(_fogModelOffset.Value, Vector3.zero), Vec(_fogModelRotation.Value, Vector3.zero), _fogModelScale.Value, "GtfoPunk_FogModel", InstanceFirst);
                if (rep != null)
                    t.Reapply.Add(() =>
                    {
                        rep.transform.localPosition = Vec(_fogModelOffset.Value, Vector3.zero);
                        rep.transform.localRotation = Quaternion.Euler(Vec(_fogModelRotation.Value, Vector3.zero));
                        rep.transform.localScale = Vector3.one * _fogModelScale.Value;
                    });
            }
            if (_fogLamp.Value > 0f)
            {
                var lgo = new GameObject("GtfoPunk_FogLamp");
                lgo.transform.SetParent(anchor.transform, false);
                lgo.transform.localPosition = Vec(_fogModelOffset.Value, Vector3.zero) + Vector3.up * 0.15f;
                var l = lgo.AddComponent<Light>();
                t.FogBulb = l;
                l.type = LightType.Point;
                l.range = 5f;
                l.intensity = _fogLamp.Value;
                l.color = new Color(1f, 0.75f, 0.45f); // warm incandescent bulb
                l.shadows = LightShadows.None;
                CL_Light.Create(eClusteredLightType.Point, lgo, l);
            }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] FT1 model: {e.Message}"); }

        // Of the thrown repeller keep only the sphere: no physics, colliders or model.
        foreach (var c in go.GetComponentsInChildren<FogRepellerInstance>(true)) UnityEngine.Object.Destroy(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        var sphere = go.GetComponentInChildren<FogRepeller_Sphere>(true);
        if (sphere == null) { UnityEngine.Object.Destroy(go); return false; }
        sphere.InfiniteDuration = true;
        sphere.Range = t.Mode == Mode.FogTurbine ? _turbineRange.Value : _fogRange.Value;
        sphere.gameObject.SetActive(true);
        sphere.StartRepelling();

        t.Extra = go;
        t.Sphere = sphere;
        return true;
    }

    // ---------- screen, light disturbance, lamp model ----------

    // ---------- battery (non-shooting sentries) ----------

    private static bool UsesBattery(Mode m) => m == Mode.Light || m == Mode.Fog || m == Mode.FogTurbine || m == Mode.Tracker;

    /// <summary>Drains the sentry ammo as a battery (same rate on every machine; the host's value is synced anyway) and switches the sentry off/on.</summary>
    private static void Battery(Turret t)
    {
        if (!_battery.Value || !UsesBattery(t.Mode)) { if (t.Flat) PowerOn(t); return; }
        float seconds = t.Mode switch { Mode.Light => _batteryLight.Value, Mode.Tracker => _batteryTracker.Value, _ => _batteryFog.Value };
        float cap = t.Inst.AmmoMaxCap;
        if (seconds > 0f && cap > 0f && t.Inst.m_ammo > 0f)
            t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - cap / seconds * Time.deltaTime);
        bool empty = t.Inst.m_ammo <= 0.0001f;
        if (empty && !t.Flat) PowerOff(t);
        else if (!empty && t.Flat) PowerOn(t);
    }

    private static void PowerOff(Turret t)
    {
        t.Flat = true;
        if (t.Light != null) t.Light.enabled = false;
        if (t.Glow != null) t.Glow.enabled = false;
        if (t.FogBulb != null) t.FogBulb.enabled = false;
        try { t.Sphere?.StopRepelling(); } catch { }
        try { CellSound.Post(AK.EVENTS.BIOTRACKER_TOOL_LOOP_STOP, t.Inst.transform.position); } catch { }
        ModInfo.Log.LogInfo($"[TurretVariants] {t.Mode}: battery empty — switched off");
    }

    private static void PowerOn(Turret t)
    {
        t.Flat = false;
        if (t.Light != null) t.Light.enabled = true;
        if (t.Glow != null) t.Glow.enabled = true;
        if (t.FogBulb != null) t.FogBulb.enabled = true;
        try { if (t.Sphere != null) { t.Sphere.InfiniteDuration = true; t.Sphere.StartRepelling(); } } catch { }
    }

    private static void UpdateScreen(Turret t)
    {
        if (t.Screen == null)
        {
            var scr = t.Inst.m_screen == null ? null : ((Il2CppObjectBase)t.Inst.m_screen).TryCast<SentryGunScreen>();
            t.Screen = scr?.m_ammoText;
            if (t.Screen == null) return;
            // One line, shrink to fit (long labels like "FT1-T 84%" used to wrap and push the text up).
            try
            {
                float size = t.Screen.fontSize;
                t.Screen.enableWordWrapping = false;
                t.Screen.enableAutoSizing = true;
                t.Screen.fontSizeMax = size;
                t.Screen.fontSizeMin = size * 0.3f;
            }
            catch { }
        }
        string text = t.Mode switch
        {
            Mode.Light => t.Variant == 0 ? "LT2" : "LT2-" + (char)('A' + t.Variant - 1),
            Mode.FogTurbine => "FT1-T",
            Mode.Tracker => t.Tagged > 0 ? $"BT3 [{t.Tagged}]" : "BT3",
            Mode.Foam => "CF4",
            Mode.Grenade => "GLF-3",
            _ => "FT1",
        };
        if (t.Mode == Mode.Grenade)
        {
            // A counted weapon: grenades left.
            float cost = t.Inst.CostOfBullet * _glAmmo.Value;
            text = cost > 0f ? $"{text} {Mathf.FloorToInt(t.Inst.m_ammo / cost + 0.001f)}" : text;
        }
        else if (t.Mode == Mode.Foam)
            text = t.Inst.AmmoMaxCap > 0f ? $"{text} {Mathf.CeilToInt(100f * t.Inst.m_ammo / t.Inst.AmmoMaxCap)}%" : text; // a tank: % left
        else if (_battery.Value && t.Inst.AmmoMaxCap > 0f)
            text = t.Flat ? text + " OFF" : $"{text} {Mathf.CeilToInt(100f * t.Inst.m_ammo / t.Inst.AmmoMaxCap)}%";
        if (t.Screen.text != text) t.Screen.text = text;
    }

    private static void Disturb(Turret t)
    {
        if (!Enum.TryParse<NM_NoiseType>(_disturb.Value, true, out var type)) return; // Off
        var tr = t.Light.transform;
        float range = _lightRange.Value, half = _lightAngle.Value * 0.5f;
        INM_NoiseMaker maker = null;
        // Il2Cpp: get the interface via Cast (TryCast on an interface returns null — that's why the light used to disturb nobody).
        try { var owner = t.Inst.Owner; if (owner != null) maker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>(); } catch { }
        if (maker == null) return;
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(tr.position, range, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var to = enemy.EyePosition - tr.position;
            if (Vector3.Angle(tr.forward, to) > half) continue;           // not in the beam
            if (Physics.Linecast(tr.position, enemy.EyePosition, LayerManager.MASK_WORLD)) continue; // behind a wall
            var node = enemy.CourseNode;
            if (node == null) continue;
            if (!t.LitSince.TryGetValue(enemy.Pointer, out var since)) t.LitSince[enemy.Pointer] = since = Time.time;
            bool wake = _lightWakeAfter.Value > 0f && Time.time - since >= _lightWakeAfter.Value;
            MakeNoiseAt(maker, enemy, wake ? NM_NoiseType.InstaDetect : type);
        }
        // Whoever left the beam — reset their timer.
        foreach (var k in t.LitSince.Keys.Where(k => !seen.Contains(k)).ToList()) t.LitSince.Remove(k);
    }

    // ---------- FT1 / FT1-T: the hum of the repeller wakes sleepers nearby ----------

    private static int _humLogs;

    /// <summary>
    /// Sleeping enemies within HumRadius hear the fog repeller (through walls, it's a machine hum):
    /// a disturbance every HumInterval, and an enemy that keeps hearing it for HumWakeAfter seconds wakes up.
    /// </summary>
    private static void Hum(Turret t)
    {
        if (!Enum.TryParse<NM_NoiseType>(_humNoise.Value, true, out var type) || _humRadius.Value <= 0f) return; // Off
        INM_NoiseMaker maker = null;
        try { var owner = t.Inst.Owner; if (owner != null) maker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>(); } catch { }
        if (maker == null) return;
        var pos = t.Inst.transform.position + Vector3.up * 0.5f;
        var seen = new HashSet<IntPtr>();
        int woke = 0;
        foreach (var col in Physics.OverlapSphere(pos, _humRadius.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer) || enemy.CourseNode == null) continue;
            if (!t.LitSince.TryGetValue(enemy.Pointer, out var since)) t.LitSince[enemy.Pointer] = since = Time.time;
            bool wake = _humWakeAfter.Value > 0f && Time.time - since >= _humWakeAfter.Value;
            if (wake) woke++;
            MakeNoiseAt(maker, enemy, wake ? NM_NoiseType.InstaDetect : type);
        }
        foreach (var k in t.LitSince.Keys.Where(k => !seen.Contains(k)).ToList()) t.LitSince.Remove(k);
        if (seen.Count > 0 && _humLogs++ < 5) ModInfo.Log.LogInfo($"[TurretVariants] {t.Mode} hum: {seen.Count} enemies within {_humRadius.Value:0} m, woken {woke}");
    }

    private static void MakeNoiseAt(INM_NoiseMaker maker, EnemyAgent enemy, NM_NoiseType type) =>
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

    // ---------- tagged targets: combat sentries get better against what BT3 (or any bio-tracker) tagged ----------

    private static bool _tagBonusApplied;

    /// <summary>
    /// The game already has "tagged target" multipliers in every sentry archetype (damage, stagger, rotation speed,
    /// start-fire delay, tag priority) — mostly left at 1. Scale them up so tagging pays off for the sentries.
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
            string before = $"dmg ×{a.Sentry_DamageTagMulti:0.##}, stagger ×{a.Sentry_StaggerDamageTagMulti:0.##}, turn ×{a.Sentry_RotationSpeedTagMulti:0.##}, start delay ×{a.Sentry_StartFireDelayTagMulti:0.##}, prio {a.Sentry_PrioTag}";
            a.Sentry_DamageTagMulti *= Math.Max(0.1f, _tagDamage.Value);
            a.Sentry_StaggerDamageTagMulti *= Math.Max(0.1f, _tagDamage.Value);
            a.Sentry_RotationSpeedTagMulti *= Math.Max(0.1f, _tagTurn.Value);
            a.Sentry_StartFireDelayTagMulti *= Mathf.Clamp(_tagDelay.Value, 0.05f, 10f);
            if (_tagPrio.Value) a.Sentry_PrioTag = true;
            ModInfo.Log.LogInfo($"[TurretVariants] tagged-target bonus for {a.name} ({mode}): {before} → dmg ×{a.Sentry_DamageTagMulti:0.##}, turn ×{a.Sentry_RotationSpeedTagMulti:0.##}, start delay ×{a.Sentry_StartFireDelayTagMulti:0.##}, prio {a.Sentry_PrioTag}");
        }
    }

    // ---------- BT3: sector scan ----------

    private static void TrackerScan(Turret t)
    {
        var origin = t.Inst.transform.position + Vector3.up * 0.6f;
        var fwd = t.Inst.transform.forward; fwd.y = 0f;
        float half = _trackerAngle.Value * 0.5f;
        var seen = new HashSet<IntPtr>();
        int count = 0, tagged = 0;
        foreach (var col in Physics.OverlapSphere(origin, _trackerRange.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var to = enemy.Position - origin; to.y = 0f;
            if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > half) continue; // outside the sector
            if (!_trackerWalls.Value && Physics.Linecast(origin, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
            count++;
            if (enemy.IsTagged || !SNetwork.SNet.IsMaster) continue;
            ToolSyncManager.WantToTagEnemy(enemy);
            tagged++;
        }
        t.Tagged = count;
        // Like the handheld bio-tracker: a beep when someone is in the sector (for all players).
        if (count > 0 && _trackerSound.Value) CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, origin);
        if (tagged > 0) ModInfo.Log.LogInfo($"[TurretVariants] BT3: in sector {count}, newly tagged {tagged}");
    }

    // ---------- CF4: foam shooting ----------

    private static bool _foamLogged, _glueGravityLogged;

    /// <summary>Target velocity — from the aim point displacement between frames (smoothed).</summary>
    private static void TrackAim(Turret t)
    {
        var det = t.Inst.m_detection;
        if (det != null && det.HasTarget && t.Inst.TryGetTargetAimPos(out var cur))
        {
            float dt = Time.time - t.LastAimTime;
            if (t.HasLastAim && dt > 0.01f && dt < 0.5f && (cur - t.LastAim).sqrMagnitude < 25f)
                t.AimVel = Vector3.Lerp(t.AimVel, (cur - t.LastAim) / dt, 0.3f);
            else if (!t.HasLastAim || dt >= 0.5f) t.AimVel = Vector3.zero;
            t.LastAim = cur; t.LastAimTime = Time.time; t.HasLastAim = true;
        }
        else t.HasLastAim = false;
    }

    /// <summary>Lead point: where the target will be when the projectile arrives.</summary>
    private static Vector3 Lead(Vector3 from, Vector3 aim, Vector3 vel, float speed, float g)
    {
        var target = aim;
        if (vel.sqrMagnitude <= 0.04f) return target;
        for (int i = 0; i < 2; i++)
        {
            var v0 = Ballistic(from, target, speed, g);
            var flat = new Vector3(v0.x, 0f, v0.z).magnitude;
            float tof = flat > 0.1f ? new Vector3(target.x - from.x, 0f, target.z - from.z).magnitude / flat : (target - from).magnitude / speed;
            target = aim + new Vector3(vel.x, 0f, vel.z) * Math.Min(tof, 1.5f);
        }
        return target;
    }

    // ---------- GLF-3: grenade launcher (was GL5) ----------

    private static bool SetupGrenade(Turret t)
    {
        var head = MakeHead(t, out _, out _);
        if (head == null) return Time.time - t.SpawnedAt > 4f;
        AttachParts(t, head.transform, _glParts, _glPartsOffset, _glPartsRotation, _glPartsScale, "GLF-3");
        return true;
    }

    private static bool _glLogged;

    private static void GrenadeTick(Turret t)
    {
        TrackAim(t);
        int mag = Math.Max(1, _glMag.Value);
        if (t.MagLeft < 0) t.MagLeft = mag;
        // Idle with a partly used drum — top it up after the reload time.
        if (t.MagLeft < mag && t.MagLeft > 0 && Time.time - t.LastShotAt > Math.Max(0.5f, _glReload.Value)) t.MagLeft = mag;
        if (Time.time < t.NextShot) return;
        if (t.MagLeft == 0) t.MagLeft = mag; // reload finished (NextShot was pushed back by the reload time)
        var det = t.Inst.m_detection;
        if (det == null || !det.HasTarget || !t.Inst.TryGetTargetAimPos(out var aim)) return;
        var owner = t.Inst.Owner;
        if (owner == null) return;
        float cost = Math.Max(0f, t.Inst.CostOfBullet * _glAmmo.Value);
        if (t.Inst.m_ammo < cost) return;

        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        var muzzle = firing?.MuzzleAlign;
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

        var node = owner.CourseNode ?? t.Inst.CourseNode;
        if (!Grenades.Throw(owner, from, Ballistic(from, target, speed, g), node))
        {
            if (!_glLogged) { _glLogged = true; ModInfo.Log.LogWarning("[TurretVariants] GLF-3: no grenade model (item 136) — sentry won't fire"); }
            t.NextShot = Time.time + 5f;
            return;
        }
        try { CellSound.Post(AK.EVENTS.GLUEGUNFIRE1SHOT, from); } catch { }
        t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - cost);
        t.MagLeft--;
        t.LastShotAt = Time.time;
        t.NextShot = Time.time + (t.MagLeft > 0 ? Math.Max(0.1f, _glShotDelay.Value) : Math.Max(0.5f, _glReload.Value));
        if (t.MagLeft == 0) try { CellSound.Post(AK.EVENTS.GLUEGUNRECHARGE, t.Inst.transform.position); } catch { }
    }

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
        if (Time.time < t.NextShot) return;
        if (!FoamShot(t)) return;
        t.BurstLeft = Math.Max(0, _foamBurst.Value - 1);
        t.NextBurst = Time.time + Math.Max(0.03f, _foamBurstDelay.Value);
        t.NextShot = Time.time + Math.Max(0.2f, _foamInterval.Value);
    }

    /// <summary>One foam glob. false — nothing to shoot at or nothing to shoot with.</summary>
    private static bool FoamShot(Turret t)
    {
        var owner = t.Inst.Owner;
        if (owner == null) return false;
        float cost = Math.Max(0f, t.Inst.CostOfBullet * _foamAmmo.Value);
        if (!_foamLogged)
        {
            _foamLogged = true;
            ModInfo.Log.LogInfo($"[TurretVariants] CF4: ammo {t.Inst.Ammo:0.##}/{t.Inst.AmmoMaxCap:0.##}, bullet cost {t.Inst.CostOfBullet:0.###}, glob cost {cost:0.###}");
        }
        if (t.Inst.m_ammo < cost) return false;

        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        var muzzle = firing?.MuzzleAlign;
        var from = muzzle != null ? muzzle.position + muzzle.forward * 0.3f : t.Inst.transform.position + Vector3.up * 0.7f;

        // Target: the one the sentry picked, unless it's already glued; otherwise the nearest unglued enemy in line of sight.
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
            have = true;
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

    // ---------- CF4: what the foam does (slow + acid + stagger) ----------

    private static float _nextFoamFx;
    private static readonly Dictionary<IntPtr, float> _nextStagger = new();
    private static int _foamFxLogs;

    /// <summary>
    /// Around every CF4: enemies with foam on them
    ///  - are kept below "stuck" (FoamMaxGlue) — slowed down, not frozen (every machine, glue is simulated locally);
    ///  - take acid damage while foamed (FoamAcidDps, host);
    ///  - get staggered now and then, which interrupts attacks (FoamStagger, host).
    /// </summary>
    private static void FoamEffects()
    {
        if (Time.time < _nextFoamFx) return;
        const float dt = 0.1f;
        _nextFoamFx = Time.time + dt;
        bool master = SNetwork.SNet.IsMaster;
        bool acidTick = master && Time.time >= _nextAcid;
        if (acidTick) _nextAcid = Time.time + 0.5f;
        var seen = new HashSet<IntPtr>();
        foreach (var t in _turrets.Values)
        {
            if (t.Mode != Mode.Foam || !t.Ready || t.Inst == null) continue;
            foreach (var col in Physics.OverlapSphere(t.Inst.transform.position, _foamRange.Value + 5f, LayerManager.MASK_ENEMY_DAMAGABLE))
            {
                var e = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
                if (e == null || !e.Alive || !seen.Add(e.Pointer)) continue;
                var d = e.Damage;
                if (d == null) continue;
                float rel = d.AttachedGlueRel;
                if (rel <= 0.02f) continue;

                float cap = _foamMaxGlue.Value;
                if (cap < 1f && rel > cap)
                {
                    float vol = d.AttachedGlueVolume;
                    float excess = vol * (rel - cap) / rel;
                    try { d.RemoveGlueVolume(excess, 0f); } catch { }
                    if (_foamFxLogs++ < 6) ModInfo.Log.LogInfo($"[TurretVariants] CF4 foam: glue {rel:0.##} → cap {cap:0.##} (stuck {d.IsStuckInGlue}, speed ×{d.GlueMoveSpeedMulti:0.##})");
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

    private static float _nextAcid;

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

    private static EnemyAgent NearestUnglued(Vector3 from, Vector3 fwd)
    {
        EnemyAgent best = null; float bestD = float.MaxValue;
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(from, _foamRange.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var e = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (e == null || !e.Alive || !seen.Add(e.Pointer) || Glued(e)) continue;
            var to = e.EyePosition - from;
            if (Vector3.Angle(fwd, to) > 75f) continue; // the sentry doesn't shoot backwards
            if (Physics.Linecast(from, e.EyePosition, LayerManager.MASK_WORLD)) continue;
            float d = to.sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    /// <summary>Velocity to hit 'to' from 'from' with the given launch speed and gravity (low arc; if out of reach — 45°).</summary>
    private static Vector3 Ballistic(Vector3 from, Vector3 to, float speed, float g)
    {
        var d = to - from;
        var flat = new Vector3(d.x, 0f, d.z);
        float x = flat.magnitude, y = d.y;
        if (g <= 0.01f || x < 0.05f) return d.normalized * speed;
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
        float angle = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI / 4f;
        var dir = flat / x;
        return (dir * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
    }

    // Log the real foam glob gravity once — to set FoamGravity.
    [HarmonyPatch(typeof(ProjectileManager), nameof(ProjectileManager.SpawnGlueGunProjectileIfNeeded))]
    private static class Patch_GlueSpawn
    {
        private static void Postfix(GlueGunProjectile __result)
        {
            if (_glueGravityLogged || __result == null) return;
            _glueGravityLogged = true;
            try { ModInfo.Log.LogInfo($"[TurretVariants] foam: glob gravity {__result.m_gravity}"); } catch { }
        }
    }

    private static Vector2 Vec2(string s)
    {
        var p = (s ?? "").Split(',');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float x = 0f, y = 0f;
        if (p.Length > 0) float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out x);
        if (p.Length > 1) float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out y);
        return new Vector2(x, y);
    }

    private static Vector3 Vec(string s, Vector3 def)
    {
        var p = s.Split(',');
        if (p.Length != 3) return def;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out var x) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var y)
            && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var z) ? new Vector3(x, y, z) : def;
    }

    private static uint _lampId = uint.MaxValue;

    private static uint LampItemId()
    {
        if (_lampId != uint.MaxValue) return _lampId;
        _lampId = 0;
        string want = new string(_lampItem.Value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (want.Length == 0) return 0;
        foreach (var b in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
        {
            if (b == null) continue;
            foreach (var n in new[] { b.publicName, b.name })
            {
                string nn = new string((n ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (nn.Length > 0 && nn.Contains(want)) { _lampId = b.persistentID; ModInfo.Log.LogInfo($"[TurretVariants] lamp model: {b.publicName} ({b.persistentID})"); return _lampId; }
            }
        }
        ModInfo.Log.LogWarning($"[TurretVariants] item '{_lampItem.Value}' not found — searchlight sentry without a model");
        return 0;
    }

    private static readonly ItemMode[] PickupFirst = { ItemMode.Pickup, ItemMode.ThirdPerson, ItemMode.Instance, ItemMode.FirstPerson };
    private static readonly ItemMode[] InstanceFirst = { ItemMode.Instance, ItemMode.Pickup, ItemMode.ThirdPerson, ItemMode.FirstPerson };
    private static readonly ItemMode[] AnyMode = PickupFirst;

    private static GameObject LampPrefab(uint id, ItemMode[] order = null)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return null;
        foreach (var mode in order ?? PickupFirst)
        {
            if (perMode.Length <= (int)mode) continue;
            var dict = perMode[(int)mode];
            if (dict == null || !dict.ContainsKey(id)) continue;
            var list = dict[id];
            if (list == null) continue;
            // Take the first model variant that actually has meshes (some items have an empty third-person model).
            for (int k = 0; k < list.Count; k++)
                if (list[k] != null && list[k].GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    ModInfo.Log.LogInfo($"[TurretVariants] item {id} model: mode {mode}, variant {k}");
                    return list[k];
                }
        }
        return null;
    }

    /// <summary>
    /// Searches all loaded meshes for an object whose name (or an ancestor's name) contains one of the words.
    /// Needed for the turbine: item 133's models have no meshes, the turbine itself is apparently loaded separately.
    /// </summary>
    private static GameObject FindLoadedModel(string keywords)
    {
        var keys = (keywords ?? "").Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToArray();
        if (keys.Length == 0) return null;
        bool Match(string n) { n = (n ?? "").ToLowerInvariant(); return keys.Any(n.Contains); }
        Transform best = null; int bestCount = 0;
        foreach (var mf in AllMeshFilters())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            // The topmost ancestor with a matching name is the model root.
            Transform root = null;
            for (var tr = mf.transform; tr != null; tr = tr.parent) if (Match(tr.name)) root = tr;
            if (root == null && Match(mf.sharedMesh.name)) root = mf.transform;
            if (root == null || root.name.StartsWith("GtfoPunk_")) continue;
            int n = root.GetComponentsInChildren<MeshFilter>(true).Length;
            if (n > bestCount) { best = root; bestCount = n; }
        }
        if (best != null) ModInfo.Log.LogInfo($"[TurretVariants] turbine model found in memory: '{best.name}', meshes {bestCount}");
        return best?.gameObject;
    }

    private static IEnumerable<MeshFilter> AllMeshFilters()
    {
        var arr = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<MeshFilter>());
        if (arr == null) yield break;
        foreach (var o in arr) { var mf = o?.TryCast<MeshFilter>(); if (mf != null) yield return mf; }
    }

    private static void DumpLoadedMeshes(string keywords, List<string> lines)
    {
        var keys = (keywords ?? "").Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToArray();
        var seen = new HashSet<string>();
        foreach (var mf in AllMeshFilters())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            string path = "";
            for (var tr = mf.transform; tr != null; tr = tr.parent) path = tr.name + (path.Length > 0 ? "/" + path : "");
            string all = (path + " " + mf.sharedMesh.name).ToLowerInvariant();
            if (!keys.Any(all.Contains) || !seen.Add(path)) continue;
            lines.Add($"  mesh in memory: {path} [{mf.sharedMesh.name}]");
            if (seen.Count >= 40) break;
        }
        if (seen.Count == 0) lines.Add($"  no meshes matching '{keywords}' in memory");
    }

    /// <summary>Logs in detail what the item's loaded models contain (to find the turbine model).</summary>
    private static void DumpItemModels(uint id, List<string> lines)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return;
        for (int m = 0; m < perMode.Length; m++)
        {
            var dict = perMode[m];
            if (dict == null || !dict.ContainsKey(id)) continue;
            var list = dict[id];
            for (int k = 0; list != null && k < list.Count; k++)
            {
                var go = list[k];
                if (go == null) { lines.Add($"  item {id} mode {(ItemMode)m} [{k}]: empty"); continue; }
                var rs = go.GetComponentsInChildren<Renderer>(true);
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                var trs = go.GetComponentsInChildren<Transform>(true);
                var comps = go.GetComponentsInChildren<Component>(true).Select(c => c == null ? "null" : c.GetIl2CppType().Name).GroupBy(n => n).Select(g => $"{g.Key}x{g.Count()}");
                lines.Add($"  item {id} mode {(ItemMode)m} [{k}] '{go.name}': nodes {trs.Length}, renderers {rs.Length}, meshes {mfs.Length}; components: {string.Join(", ", comps)}");
                foreach (var tr in trs.Take(40)) lines.Add($"    - {tr.name} (pos {tr.localPosition}, scale {tr.localScale})");
            }
        }
    }

    /// <summary>Mounts a copy of the lamp model on the sentry: meshes only, no scripts, physics or colliders.</summary>
    private static GameObject AttachLamp(Transform parent) =>
        AttachModel(parent, LampItemId(), Vec(_lampOffset.Value, Vector3.zero), Vec(_lampRotation.Value, Vector3.zero), _lampScale.Value, "GtfoPunk_Lamp");

    private static GameObject AttachModel(Transform parent, uint id, Vector3 offset, Vector3 euler, float scale, string name, ItemMode[] order = null)
    {
        if (id == 0) return null;
        var prefab = LampPrefab(id, order);
        if (prefab == null && id == FogTurbineItemId) prefab = FindLoadedModel(_turbineKeywords.Value);
        if (prefab == null) { ModInfo.Log.LogWarning($"[TurretVariants] item {id} model not loaded"); return null; }

        var holder = CopyModel(prefab, parent, name);
        holder.transform.localPosition = offset;
        holder.transform.localRotation = Quaternion.Euler(euler);
        holder.transform.localScale = Vector3.one * scale;
        return holder;
    }

    /// <summary>Model copy: meshes only, no scripts, physics or colliders.</summary>
    internal static GameObject CopyModel(GameObject prefab, Transform parent, string name)
    {
        var holder = new GameObject(name);
        holder.SetActive(false); // inactive for now — the copy's scripts won't start
        holder.transform.SetParent(parent, false);

        var copy = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        for (int pass = 0; pass < 3; pass++) // several passes: some components depend on others
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<MeshRenderer>() != null || c.TryCast<SkinnedMeshRenderer>() != null) continue;
                try { UnityEngine.Object.DestroyImmediate(c); } catch { }
            }
        int layer = parent.gameObject.layer;
        foreach (var tr in copy.GetComponentsInChildren<Transform>(true)) { tr.gameObject.layer = layer; tr.gameObject.SetActive(true); }
        int n = 0;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.forceRenderingOff = false; n++; }
        holder.SetActive(true);
        ModInfo.Log.LogInfo($"[TurretVariants] model {name}: {n} meshes, layer {layer}");
        return holder;
    }

    // ---------- measuring real repellers ----------

    private static string Describe(FogRepeller_Sphere f) =>
        $"radius {f.Range:0.##} m, lasts {(f.InfiniteDuration ? "forever" : f.LifeDuration.ToString("0.#") + " s")}, " +
        $"grows {f.GrowDuration:0.#} s, shrinks {f.ShrinkDuration:0.#} s, density {f.Density:0.##}";

    private static void DumpFogRepellers()
    {
        var lines = new List<string> { $"# Fog repellers ({DateTime.Now:yyyy-MM-dd HH:mm}), level: {RundownManager.ActiveExpedition?.Descriptive?.PublicName}" };
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode != null)
            for (int m = 0; m < perMode.Length; m++)
            {
                var dict = perMode[m];
                if (dict == null) continue;
                foreach (var kv in dict)
                {
                    var list = kv.Value;
                    if (list == null || list.Count == 0 || list[0] == null) continue;
                    foreach (var f in list[0].GetComponentsInChildren<FogRepeller_Sphere>(true))
                    {
                        var item = GameDataBlockBase<ItemDataBlock>.GetBlock(kv.Key);
                        lines.Add($"model: {item?.publicName ?? kv.Key.ToString()} ({kv.Key}), mode {(ItemMode)m}: {Describe(f)}");
                    }
                }
            }
        DumpItemModels(FogTurbineItemId, lines);
        DumpLoadedMeshes(_turbineKeywords.Value + ",fog,repell", lines);
        foreach (var g in UnityEngine.Object.FindObjectsOfType<HeavyFogRepellerGlobalState>())
            if (g != null && g.m_repellerSphere != null) lines.Add($"in level: turbine '{g.gameObject.name}': {Describe(g.m_repellerSphere)}");
        foreach (var f in UnityEngine.Object.FindObjectsOfType<FogRepeller_Sphere>())
            if (f != null) lines.Add($"in level: '{f.gameObject.name}' ({f.CurrentState}, now {f.m_currentRange:0.##} m): {Describe(f)}");
        if (lines.Count == 1) lines.Add("no repellers found (models not loaded on this level)");
        foreach (var l in lines) ModInfo.Log.LogInfo($"[Fog] {l}");
        System.IO.File.AppendAllLines(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.FogRepellers.txt"), lines);
    }
}
