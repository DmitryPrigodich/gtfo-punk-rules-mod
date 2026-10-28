using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Settings. General ones live in [TurretVariants]; each sentry kind has its own section [TurretVariants.&lt;Kind&gt;]
/// with the same core keys — Enabled, Name, Label, Type, ModelOffset / ModelRotation / ModelScale (the model in the
/// cradle), Bulb / BulbOffset, BatterySeconds (non-shooting kinds), Parts (kinds with gear parts in the cradle) —
/// plus its own. Values from the old flat keys (before 0.9.0) are carried over once.
/// </summary>
public partial class TurretVariants
{
    /// <summary>Settings every sentry kind has.</summary>
    private static readonly Dictionary<Mode, string> TypeNames = new()
    {
        [Mode.Light] = "Searchlight Sentry", [Mode.Fog] = "Fog Repeller Sentry", [Mode.FogTurbine] = "Fog Turbine Sentry",
        [Mode.Tracker] = "Bio-tracker Sentry", [Mode.Foam] = "Foam Sentry", [Mode.Grenade] = "Grenade Sentry",
    };

    private class Kind
    {
        public Mode Mode;
        public string Section;
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<string> Name, Label, Type;
        public ConfigEntry<string> ModelOffset, ModelRotation;
        public ConfigEntry<float> ModelScale;
        public ConfigEntry<float> BatterySeconds; // null for kinds that shoot (they use ammo as ammo)
        public ConfigEntry<float> Bulb;
        public ConfigEntry<string> BulbOffset;

        public Vector3 Offset => Vec(ModelOffset.Value);
        public Quaternion Rotation => Quaternion.Euler(Vec(RotationText));
        public string RotationText => ModelRotation?.Value ?? "0,0,0";
        public float Scale => ModelScale.Value;

        /// <summary>Puts a model (or a parts holder) in its configured place.</summary>
        public void Place(GameObject go)
        {
            if (go == null) return;
            go.transform.localPosition = Offset;
            go.transform.localRotation = Rotation;
            go.transform.localScale = Vector3.one * Scale;
        }
    }

    private static Kind _light, _fog, _turbine, _tracker, _foam, _grenade;

    private static Kind KindOf(Mode m) => m switch
    {
        Mode.Light => _light,
        Mode.FogTurbine => _turbine,
        Mode.Tracker => _tracker,
        Mode.Foam => _foam,
        Mode.Grenade => _grenade,
        _ => _fog,
    };

    // General
    private static ConfigEntry<bool> _liveReload, _hideGun, _hideSensor, _battery, _tagPrio;
    private static ConfigEntry<float> _tagDamage, _tagTurn, _tagDelay;
    // TL1 searchlight
    private static ConfigEntry<string> _lampItem, _lightNoise;
    private static ConfigEntry<float> _lightRange, _lightAngle, _lightIntensity, _lightForward, _lensGlow, _lensGlowForward, _lightInterval, _lightWakeAfter;
    // TF I / TF II fog repellers
    private static ConfigEntry<float> _fogRadius, _turbineRadius;
    /// <summary>If the turbine item has no model: loaded meshes whose name contains this.</summary>
    private const string TurbineModelSearch = "defoggerbig";
    private static ConfigEntry<string> _fogHum, _turbineHum;
    private static ConfigEntry<float> _fogHumRadius, _fogHumInterval, _fogHumWake, _turbineHumRadius, _turbineHumInterval, _turbineHumWake;
    // TB1 bio-tracker
    private static ConfigEntry<string> _trackerParts;
    private static ConfigEntry<float> _trackerRange, _trackerAngle, _trackerInterval;
    private static ConfigEntry<bool> _trackerSound, _trackerWalls;
    // TCF3 foam
    private static ConfigEntry<string> _foamParts, _tankItem, _tankOffset, _tankRotation;
    private static ConfigEntry<bool> _tankMirror;
    private static ConfigEntry<float> _tankScale, _foamRange, _foamInterval, _foamBurstDelay, _foamSpeed, _foamGravity, _foamAmmo, _foamExpand;
    private static ConfigEntry<float> _foamMaxGlue, _foamAcid, _foamStagger, _foamStaggerInterval;
    private static ConfigEntry<int> _foamBurst;
    private static ConfigEntry<bool> _foamLead, _foamSkipGlued;
    // TGL3 grenade launcher
    private static ConfigEntry<string> _glParts;
    private static ConfigEntry<int> _glBurst;
    private static ConfigEntry<float> _glShotDelay, _glReload, _glSpeed, _glRange, _glMinRange, _glSafe, _glAmmo;

    protected override void OnBindConfig()
    {
        // ----- general -----
        _liveReload = Bind("LiveReload", true, "Re-read this config while the game runs: edit model positions, save the file — models on placed sentries move right away.");
        _hideGun = Bind("HideGun", true, "Hide the autocannon barrel on sentries that don't shoot bullets (the tripod and body stay).");
        _hideSensor = Bind("HideSensor", false, "Also hide the targeting sensor on those sentries (it may float in the air). The scan beam stays.");
        _battery = Bind("Battery", true, "Non-shooting sentries run on their ammo as a battery (see BatterySeconds of each kind); at zero they switch off. Recharge: pick up, tool refill, place again.");
        _tagDamage = BindMigrated(Id, "TaggedDamageBonus", 1f, "Combat sentries vs tagged enemies (TB1, bio-tracker): extra damage and stagger multiplier (1 — as in the game).", (Id, "TaggedDamageBonus2", null));
        _tagTurn = Bind("TaggedTurnBonus", 1.5f, "Combat sentries vs tagged enemies: turning speed multiplier (they lock on faster).");
        _tagDelay = Bind("TaggedStartDelay", 0.5f, "Combat sentries vs tagged enemies: multiplier of the delay before opening fire (less — quicker).");
        _tagPrio = Bind("TaggedPriority", true, "Combat sentries prefer tagged enemies.");
        BindStatus();

        // ----- TL1 searchlight -----
        _light = MakeKind(Mode.Light, "Searchlight", "DRED GARAGE TL1", "TL1", "LightTurret",
            "-0.06,0.05,0.145", "LampOffset3", "0,-0.02,0.04",
            "270,270,-7", "LampRotation4", "90,270,0",
            1f, "LampScale", "BatterySecondsLT2");
        _lampItem = K(_light, "Model", "Long Range Flashlight", "Item whose model is the lamp (name or part of it; empty — no model).", "LampModel");
        _lightRange = K(_light, "Range", 30f, "Beam range (m).", "LightRange");
        _lightAngle = K(_light, "Angle", 45f, "Beam angle (degrees).", "LightAngle");
        _lightIntensity = K(_light, "Brightness", 1.1f, "Beam brightness (×1.6 while an enemy is in the sector).", "LightIntensity3");
        _lightForward = K(_light, "LightForward", 0.2f, "How far in front of the lamp model the light source sits (m), so it doesn't light the lamp itself.", "LightForward", "0.35");
        _lensGlow = K(_light, "LensGlow", 2f, "Lens glow (a small light right in front of the glass). 0 — none.", "LampGlow", "1.5");
        _lensGlowForward = K(_light, "LensGlowForward", 0.2f, "How far in front of the lamp model the lens glow sits (m).", "LampGlowForward");
        _lightNoise = K(_light, "Disturbs", "Detectable", "What the beam does to enemies in it: Detectable — disturbs (like footsteps nearby), PulseOnly — pulse only, Off — nothing.", "LightDisturbs2");
        _lightInterval = K(_light, "DisturbInterval", 1.5f, "How often the beam disturbs enemies in it (s).", "LightDisturbInterval");
        _lightWakeAfter = K(_light, "WakeAfter", 2f, "A sleeper that stays in the beam this many seconds in a row wakes up. 0 — never.", "LightWakeAfter", "3");

        // ----- TF I fog repeller -----
        _fog = MakeKind(Mode.Fog, "FogRepeller", "DRED GARAGE TF I", "TF I", "FogTurret",
            "0,0.29,-0.07", "FogModelOffset2", "0,0.3,-0.05",
            "0,0,0", "FogModelRotation", "0,0,0",
            0.75f, "FogModelScale", "BatterySecondsFT1", bulb: 0.3f);
        _fogRadius = K(_fog, "Radius", 10f, "Radius of the fog-free sphere (m). 10 — like the real fog repeller.", "FogRange2");
        (_fogHum, _fogHumRadius, _fogHumInterval, _fogHumWake) = BindHum(_fog);

        // ----- TF II fog repeller with a turbine -----
        _turbine = MakeKind(Mode.FogTurbine, "FogTurbine", "DRED GARAGE TF II", "TF II", "FogTurbineTurret",
            "0.02,-0.31,-0.18", "TurbineSeat3", "0.15,-0.6,-0.05",
            "0,0,0", null, null,
            0.3f, "TurbineScale", null, bulb: 0.3f);
        _turbineRadius = K(_turbine, "Radius", 10f, "Radius of the fog-free sphere (m).", "TurbineRange2");
        (_turbineHum, _turbineHumRadius, _turbineHumInterval, _turbineHumWake) = BindHum(_turbine);

        // ----- TB1 bio-tracker -----
        _tracker = MakeKind(Mode.Tracker, "BioTracker", "DRED GARAGE TB1", "TB1", "TrackerTurret",
            "0.015,0.05,0.075", "TrackerPartsOffset", "0,0,0",
            "0,0,0", "TrackerPartsRotation", "0,0,0",
            1f, "TrackerPartsScale", "BatterySecondsBT3");
        _trackerParts = K(_tracker, "Parts", "33=3", "Tool parts in the cradle instead of the barrel ('type=ID[/subtype]', comma-separated; see GtfoPunk.GearParts.txt). Empty — keep the barrel.", "TrackerParts");
        _trackerRange = K(_tracker, "Range", 25f, "Scan range (m).", "TrackerRange");
        _trackerAngle = K(_tracker, "Angle", 90f, "Sector width (degrees, horizontal, from the placement direction).", "TrackerAngle");
        _trackerInterval = K(_tracker, "Interval", 3f, "How often it scans (s).", "TrackerInterval");
        _trackerSound = K(_tracker, "Sound", true, "Beeps like a bio-tracker when enemies are in the sector.", "TrackerSound");
        _trackerWalls = K(_tracker, "ThroughWalls", true, "Sees through walls. false — line of sight only.", "TrackerThroughWalls");

        // ----- TCF3 foam -----
        _foam = MakeKind(Mode.Foam, "Foam", "DRED GARAGE TCF3", "TCF3", "FoamTurret",
            "0,0.12,0.1", "FoamGunOffset2", "0,0,0.1",
            "0,0,180", "FoamGunRotation", "0,0,0",
            0.75f, "FoamGunScale", null, bulb: 0.2f);
        _foamParts = K(_foam, "Parts", "33=4/4", "C-foam launcher parts in the cradle instead of the barrel ('type=ID[/subtype]'). Empty — keep the barrel.", "FoamGunParts2");
        // The tank: one model now (0.9.17) — new keys, the old mirrored-grenade ones (TankModel, TankOffset …) are dropped.
        // Defaults: one C-Foam Grenade as the tank, as tuned in play (29.10).
        _tankItem = BindIn("Foam", "Tank", "C-Foam Grenade", "The foam tank: an item whose model is used (name or part of it; 'Fog Turbine' — the turbine's barrel; empty — no tank).");
        _tankMirror = BindIn("Foam", "TankMirror", false, "Two tanks, mirrored left and right (x of TankModelOffset on both sides). false — one tank.");
        _tankOffset = BindIn("Foam", "TankModelOffset", "0.035,0.025,0.125", "Tank position, from the barrel cradle (x — sideways, y — up, z — forward; m).");
        _tankRotation = BindIn("Foam", "TankModelRotation", "79,90,177", "Tank rotation (degrees x,y,z).");
        _tankScale = BindIn("Foam", "TankModelScale", 1.1f, "Tank size (1 — as is).");
        _foamRange = K(_foam, "Range", 20f, "Doesn't fire beyond this distance (m).", "FoamRange");
        _foamInterval = K(_foam, "Interval", 1.5f, "Pause between bursts (s).", "FoamInterval2");
        _foamBurst = K(_foam, "Burst", 3, "Globs per burst.", "FoamBurst");
        _foamBurstDelay = K(_foam, "BurstDelay", 0.12f, "Pause between globs in a burst (s).", "FoamBurstDelay");
        _foamSpeed = K(_foam, "Speed", 30f, "Glob launch speed (m/s).", "FoamSpeed2");
        _foamGravity = K(_foam, "Gravity", 9f, "Glob gravity used for aiming (m/s²; 9 in the game).", "FoamGravity2");
        _foamLead = K(_foam, "Lead", true, "Lead moving targets.", "FoamLead");
        _foamAmmo = K(_foam, "AmmoPerShot", 2f, "Sentry 'bullets' per glob.", "FoamAmmoPerShot3");
        _foamExpand = K(_foam, "Expand", -1f, "Glob expansion volume (-1 — the launcher's own).", "FoamExpand");
        _foamSkipGlued = K(_foam, "SkipGlued", true, "Don't waste foam on enemies already at the glue cap.", "FoamSkipGlued");
        _foamMaxGlue = K(_foam, "MaxGlue", 0.75f, "Foam slows instead of freezing: glue is kept below this share of 'stuck' (1 — freeze).", "FoamMaxGlue");
        _foamAcid = K(_foam, "AcidDps", 3f, "Acid damage per second to enemies with foam on them (0 — off).", "FoamAcidDps");
        _foamStagger = K(_foam, "Stagger", 20f, "Stagger multiplier of the hits — interrupts attacks (0 — off).", "FoamStagger");
        _foamStaggerInterval = K(_foam, "StaggerInterval", 2f, "How often one enemy can be staggered (s).", "FoamStaggerInterval");

        // ----- TGL3 grenade launcher -----
        _grenade = MakeKind(Mode.Grenade, "Grenade", "DRED GARAGE TGL3", "TGL3", "GrenadeTurret",
            "0,0.035,0.085", "GrenadePartsOffset", "0,0,0",
            "0,0,0", "GrenadePartsRotation", "0,0,0",
            0.7f, "GrenadePartsScale2", null);
        _glParts = K(_grenade, "Parts", "33=2,37=1", "Mine deployer parts in the cradle ('type=ID[/subtype]'). Empty — keep the barrel.", "GrenadeParts");
        _glBurst = K(_grenade, "Burst", 3, "Grenades per burst.", "GrenadeMagazine2");
        _glShotDelay = K(_grenade, "ShotDelay", 0.35f, "Pause between grenades in a burst (s).", "GrenadeShotDelay");
        _glReload = K(_grenade, "Reload", 2.5f, "Reload after a burst (s); a partly used burst also reloads after this long without a target.", "GrenadeReload2");
        _glSpeed = K(_grenade, "Speed", 16f, "Launch speed (m/s).", "GrenadeSpeed");
        _glRange = K(_grenade, "Range", 20f, "Doesn't fire beyond this distance (m).", "GrenadeRange", "25");
        _glMinRange = K(_grenade, "MinRange", 3f, "Doesn't fire closer than this (m).", "GrenadeMinRange", "4");
        _glSafe = K(_grenade, "PlayerSafe", 4f, "Doesn't fire if a player is within this distance of the impact point (m).", "GrenadePlayerSafe");
        _glAmmo = K(_grenade, "AmmoPerShot", 12f, "Sentry 'bullets' per grenade.", "GrenadeAmmoPerShot");

        // Old flat keys that weren't carried over are gone now.
        ConfigMigration.DropOrphans(Config, Id);
        Config.Save();
    }

    private Kind MakeKind(Mode mode, string section, string name, string label, string oldEnabledKey,
        string offset, string oldOffsetKey, string oldOffsetDefault,
        string rotation, string oldRotationKey, string oldRotationDefault,
        float scale, string oldScaleKey, string oldBatteryKey, float bulb = 0f)
    {
        var k = new Kind { Mode = mode, Section = section };
        k.Enabled = K(k, "Enabled", true, $"Add this sentry ({name}) to the tool choices. Takes effect after a game restart.", oldEnabledKey);
        k.Name = BindIn(section, "Name", name, "Name in the loadout (after a game restart).");
        k.Label = BindIn(section, "Label", label, "Short name on the sentry's screen.");
        k.Type = BindIn(section, "Type", TypeNames[mode], "Sentry type shown above its name in the weapon list (after a game restart).");
        k.ModelOffset = K(k, "ModelOffset", offset, "Model position in the barrel cradle: x — sideways, y — up, z — forward (m).", oldOffsetKey, oldOffsetDefault);
        k.ModelRotation = K(k, "ModelRotation", rotation, "Model rotation (degrees x,y,z).", oldRotationKey, oldRotationDefault);
        k.ModelScale = K(k, "ModelScale", scale, "Model size (1 — as is).", oldScaleKey);
        if (oldBatteryKey != null || mode == Mode.FogTurbine)
            k.BatterySeconds = K(k, "BatterySeconds", 300f, "How long a full battery lasts (s).", oldBatteryKey ?? "BatterySecondsFT1");
        k.Bulb = mode == Mode.Fog ? K(k, "Bulb", bulb, "Brightness of a dim warm bulb on the sentry (off with the battery). 0 — none.", "FogLamp", "0.5")
                                  : BindIn(section, "Bulb", bulb, "Brightness of a dim warm bulb on the sentry (off with the battery). 0 — none.");
        k.BulbOffset = BindIn(section, "BulbOffset", "0,0.45,-0.06", "Where the bulb sits, from the barrel cradle (x — sideways, y — up, z — forward; m).");
        return k;
    }

    private (ConfigEntry<string>, ConfigEntry<float>, ConfigEntry<float>, ConfigEntry<float>) BindHum(Kind k) => (
        K(k, "Hum", "Detectable", "The hum heard by enemies nearby, through walls: Detectable — disturbs, PulseOnly — pulse only, Off — silent.", "FogHum"),
        K(k, "HumRadius", 6f, "How far the hum is heard (m).", "FogHumRadius"),
        K(k, "HumInterval", 2f, "How often the hum disturbs enemies (s).", "FogHumInterval"),
        K(k, "HumWakeAfter", 8f, "A sleeper hearing the hum this many seconds in a row wakes up. 0 — never.", "FogHumWakeAfter"));

    /// <summary>A kind's setting, taking the value of the old flat key [TurretVariants] oldKey if the player had changed it.</summary>
    private ConfigEntry<T> K<T>(Kind k, string key, T def, string desc, string oldKey = null, string oldDefault = null)
    {
        if (oldKey == null) return BindIn(k.Section, key, def, desc);
        return BindMigrated($"{Id}.{k.Section}", key, def, desc, (Id, oldKey, oldDefault ?? Serialize(def)));
    }

    private static string Serialize<T>(T v) => TomlTypeConverter.ConvertToString(v, typeof(T));

    // ---------- live config ----------

    private static System.IO.FileSystemWatcher _watcher;
    private static volatile bool _cfgChanged;
    private static float _reloadAt;

    /// <summary>When the config file changes on disk, re-read it and re-place the models of placed sentries.</summary>
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
        if (_reloadAt <= 0f || Time.unscaledTime < _reloadAt) return;
        _reloadAt = 0f;
        try
        {
            _liveReload.ConfigFile.Reload();
            int n = 0;
            foreach (var t in _turrets.Values)
                foreach (var a in t.Reapply)
                {
                    try { a(); n++; }
                    catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] re-placing: {e.Message}"); }
                }
            if (n > 0) ModInfo.Log.LogInfo($"[TurretVariants] config re-read, models re-placed: {n}");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] re-reading config: {e.Message}"); }
    }

    // ---------- parsing ----------

    /// <summary>"x,y,z" or "x,y" → vector (invariant culture); zero if malformed.</summary>
    internal static Vector3 Vec(string s)
    {
        var p = (s ?? "").Split(',');
        if (p.Length < 2 || p.Length > 3) return Vector3.zero;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var st = System.Globalization.NumberStyles.Float;
        var v = Vector3.zero;
        for (int i = 0; i < p.Length; i++)
        {
            if (!float.TryParse(p[i].Trim(), st, ci, out var f)) return Vector3.zero;
            v[i] = f;
        }
        return v;
    }
}
