using Agents;
using Enemies;
using Gear;
using GameData;
using Il2CppInterop.Runtime.InteropTypes;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "DRED SILENZIO GP6-S": silenced pistol from the "garage". Damage (lower, 15 by default) from Shelling Arid 5, sound from S49,
/// body and barrel assembled from parts of different guns (option Guns). A shot wakes sleepers only within HardRadius (10 m),
/// further out, up to SoftRadius, it only disturbs them (pulse), beyond that — silence. Not heard through walls.
/// The host computes the noise, so everyone needs the mod.
/// Silenced main-slot guns (option MainGuns): rate of fire and damage of an SMG (MainStatsFrom, Van Auken LTC5), the
/// same quiet shot; each gun gets its own category so the hand grip can come from its body. Test variants for now.
/// </summary>
public class SilencedGun : ModuleBase
{
    public override string Id => "SilencedGun";
    public override string Description => "Silenced pistol DRED SILENZIO GP6-S: Arid 5 body (damage 15 — headshots kill), S49 sound, 3RB body + Mastaba top. Only wakes enemies up close. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<string> _stats, _sound, _guns, _pose, _mainStats, _mainGuns, _mainType, _mainSound;
    private static ConfigEntry<float> _light, _mainLight;
    private static ConfigEntry<float> _hard, _soft;
    private static ConfigEntry<bool> _softPulse;

    protected override void OnBindConfig()
    {
        _stats = Bind("StatsFrom", "Shelling Arid 5", "Weapon to take damage, magazine and rate of fire from (category).");
        _sound = Bind("SoundFrom", "Shelling S49", "Weapon to take the shot sound from.");
        _pose = Bind("PoseFrom", "Mastaba R66", "Weapon to take the grip and hand pose from (pose and which part the left hand holds). Must match the gun's \"top\", otherwise the hand disappears on reload.");
        _guns = Bind("SilencedGuns", NewDefault,
            "Silenced guns separated by ';': 'Name: Body + part=from, …'. Body — the weapon whose parts are the base; 12 — barrel/top. Parts and IDs are in GtfoPunk.GearList.txt.");
        _hard = Bind("HardRadius", 10f, "Radius (m) within which a shot wakes enemies.");
        _damage = Bind("Damage", 15f, "Damage per shot. 15: a headshot kills a striker or a shooter, a body shot doesn't (Arid 5 itself does far more). 0 — Arid 5's own.");
        _archName = Bind("WeaponType", "Silenced Pistol", "Weapon type shown above the gun's name in the weapon list (the gun's own name is in SilencedGuns).");
        _light = Bind("Flashlight", 0.5f, "Flashlight strength of the silenced pistol (brightness and reach; 1 — as the part's own).");
        _mainStats = Bind("MainStatsFrom", "Van Auken LTC5", "Silenced main-slot guns: weapon to take rate of fire and magazine from (a gun can name its own: '| stats=…').");
        _mainSound = Bind("MainSoundFrom", "Raptus Treffen 2", "Silenced main-slot guns: weapon to take the shot sound from.");
        _mainLight = Bind("MainFlashlight", 0.6f, "Flashlight strength of the silenced main-slot guns (1 — as the part's own).");
        _mainGuns = Bind("MainGuns", MainDefault,
            "Silenced main-slot guns separated by ';': 'Name: Body + part=ID, … | option=value, …'. The body gives the parts and the hand grip. "
            + "Part IDs: 12 front, 16 receiver, 19 stock, 21 sight, 23 magazine (GtfoPunk.GearList.txt / GearParts.txt). "
            + "Options: stats=<gun> (rate of fire), burst=<shots> (the default fire mode; 0 — the donor's own), damage, precision, falloff=<from>-<to> m, delay=<pause between bursts, s>, clip, spread=<×>. Empty — none.");
        _mainType = Bind("MainWeaponType", "Silenced SMG", "Weapon type shown above the main-slot silenced guns' names in the weapon list.");
        _soft = Bind("SoftRadius", 25f, "Radius (m) within which a shot only disturbs (pulse) without waking. 0 — don't disturb.");
        _softPulse = Bind("SoftIsPulseOnly", true, "true — distant enemies only pulse; false — they get regular noise (may wake up after repeated shots).");
    }

    // 4 (ItemFPSSettings) — hand animations: taken from Mastaba, otherwise the hand inserting rounds disappears on reload.
    private const string NewDefault = "DRED SILENZIO GP6-S: Bataldo 3RB + 12=Mastaba R66, 4=Mastaba R66";
    /// <summary>Main-slot gun (29.10): SMG-S A — Hanaway front and magazine, HXC stock, Sight_14; Hanaway's ammo. Burst is the default mode: two headshots kill a striker at mid range
    /// (like one from GP6-S), a body burst doesn't.</summary>
    private const string MainA = "Van Auken LTC5 + 12=15, 19=3, 21=10, 23={0} | burst=3, damage=6, precision=0.95, falloff=25-45, delay=0.25, ammo=TR22 Hanaway";
    /// <summary>29.10, round 4: TR22 Hanaway's magazine (Mag_556_40rnds_A, 1 — the same part HXC has).</summary>
    private static string MainDefault => "DS SMG-S A: " + string.Format(MainA, 1);
    /// <summary>Earlier test sets, replaced by the new default.</summary>
    private static bool OldMainDefault(string v) => v == MainDefault0918 || v.StartsWith("DS SMG-S A Sight_18:") || v.StartsWith("DS SMG-S A HXC:");
    /// <summary>0.9.18's test set — replaced by the new default.</summary>
    private const string MainDefault0918 =
        "DRED SILENZIO SMG-S A: Van Auken LTC5 + 12=15, 19=3, 21=9; " +
        "DRED SILENZIO SMG-S B: Van Auken LTC5 + 12=15, 19=19, 21=11; " +
        "DRED SILENZIO SMG-S C: Accrat Golok DA + 12=46, 16=20, 19=17, 21=45";
    private const string TestDefault = NewDefault + "; GP6-S TEST B: Mastaba R66 + 19=Bataldo 3RB";

    private static readonly HashSet<uint> _categories = new();
    private static readonly Dictionary<IntPtr, float> _cooldown = new();

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnLevelExit += () => _cooldown.Clear();
    }

    private static void Inject()
    {
        foreach (var old in new[] { "AM DRED GP1-S:", "DRED SILENZIO GP7-S:" }) // renames
            if (_guns.Value.Contains(old)) _guns.Value = _guns.Value.Replace(old, "DRED SILENZIO GP6-S:");
        // Migrate old default values (temporary — including test variant B: full Mastaba + 3RB stock).
        if (_guns.Value == "DRED SILENZIO GP6-S: Bataldo 3RB + 12=Mastaba R66" || _guns.Value == TestDefault) _guns.Value = NewDefault;
        var statsGear = GearFactory.FindByName(_stats.Value);
        if (statsGear == null) { ModInfo.Log.LogWarning($"[SilencedGun] weapon '{_stats.Value}' not found — see GtfoPunk.GearList.txt"); return; }
        var sc = GearFactory.GetComps(statsGear.GearJSON);
        if (!sc.TryGetValue((int)eGearComponent.Category, out var statsCat)) return;
        uint catId = GearFactory.CloneCategory(statsCat, "GtfoPunk_SilencedGun", "DRED SILENZIO",
            Lang.T("Silenced. Wakes enemies only within 10 m; farther ones only stir.", "С глушителем. Будит только в 10 м, дальше враги лишь тревожатся."));
        _categories.Add(catId);
        OwnArchetypes(catId);

        // The hand grip comes from the category: pose and part priority for the left hand. Arid's left hand holds the magazine,
        // but the revolver-style top has no magazine — so the hand disappeared. Copy from the "top" weapon's category.
        var poseGear = GearFactory.FindByName(_pose.Value);
        if (poseGear != null && GearFactory.GetComps(poseGear.GearJSON).TryGetValue((int)eGearComponent.Category, out var poseCat))
        {
            var src = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(poseCat);
            var dst = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(catId);
            if (src != null && dst != null)
            {
                dst.FPSArmPoseName = src.FPSArmPoseName;
                dst.PartAlignPriority = src.PartAlignPriority;
                dst.ThirdPersonFullbodyMovement = src.ThirdPersonFullbodyMovement;
                ModInfo.Log.LogInfo($"[SilencedGun] hand grip from '{GearFactory.NameOf(poseGear)}' (pose {src.FPSArmPoseName})");
            }
        }

        uint? sound = null;
        var soundGear = GearFactory.FindByName(_sound.Value);
        if (soundGear != null && GearFactory.GetComps(soundGear.GearJSON).TryGetValue((int)eGearComponent.AudioSetting, out var snd)) sound = snd;
        else ModInfo.Log.LogWarning($"[SilencedGun] sound '{_sound.Value}' not found — using the body's sound");

        int idx = 0;
        foreach (var entry in _guns.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            idx++;
            var nameSplit = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (nameSplit.Length != 2) { ModInfo.Log.LogWarning($"[SilencedGun] can't parse '{entry}'"); continue; }
            var bodySplit = nameSplit[1].Split('+', 2, StringSplitOptions.TrimEntries);
            var body = GearFactory.FindByName(bodySplit[0]);
            if (body == null) { ModInfo.Log.LogWarning($"[SilencedGun] body '{bodySplit[0]}' not found"); continue; }

            var comps = new Dictionary<int, uint?>
            {
                [(int)eGearComponent.Category] = catId,
                // Fire mode and item type — same as the stats donor (otherwise the category won't find its fire mode).
                [(int)eGearComponent.FireMode] = sc.TryGetValue((int)eGearComponent.FireMode, out var fm) ? fm : null,
                [(int)eGearComponent.BaseItem] = sc.TryGetValue((int)eGearComponent.BaseItem, out var bi) ? bi : null,
            };
            if (sound.HasValue) comps[(int)eGearComponent.AudioSetting] = sound;
            if (bodySplit.Length > 1)
                foreach (var kv in GearFactory.ParseParts(bodySplit[1], "SilencedGun")) comps[kv.Key] = kv.Value;
            GearFactory.AddGear(body, $"GtfoPunk_SilencedGun_{idx}", nameSplit[0], comps);
        }
        try { InjectMain(); } catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] main-slot guns: {e.Message}"); }
    }

    /// <summary>Silenced main-slot guns: each gets a category of its own (stats donor's rate of fire, the body's hand grip),
    /// a tuned burst archetype as the default mode (FireModes switches to semi / auto from it), the main-slot sound.</summary>
    private static void InjectMain()
    {
        if (OldMainDefault(_mainGuns.Value)) _mainGuns.Value = MainDefault;
        uint? sound = null;
        var soundGear = GearFactory.FindByName(_mainSound.Value);
        if (soundGear != null && GearFactory.GetComps(soundGear.GearJSON).TryGetValue((int)eGearComponent.AudioSetting, out var snd)) sound = snd;
        foreach (var entry in _mainGuns.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { AddGun(entry, silent: true, "GtfoPunk_SilencedMain_", _mainStats.Value, _mainType.Value, sound); }
            catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] '{entry}': {e.Message}"); }
        }
    }

    /// <summary>
    /// A garage gun from 'Name: Body + parts | options': a category of its own (rate of fire from the stats donor, hand
    /// grip from the body), the burst option as the default mode. <paramref name="silent"/> — quiet shot and a weaker
    /// flashlight (DRED SILENZIO); otherwise a normal loud gun (DRED BULL …). Option type= — the weapon type shown.
    /// </summary>
    internal static void AddGun(string entry, bool silent, string keyPrefix, string defaultStats, string defaultType, uint? sound)
    {
        var nameSplit = entry.Split(':', 2, StringSplitOptions.TrimEntries);
        if (nameSplit.Length != 2) { ModInfo.Log.LogWarning($"[SilencedGun] can't parse '{entry}'"); return; }
        var optSplit = nameSplit[1].Split('|', 2, StringSplitOptions.TrimEntries);
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (optSplit.Length > 1)
            foreach (var o in optSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = o.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2) opt[kv[0]] = kv[1];
            }
        var bodySplit = optSplit[0].Split('+', 2, StringSplitOptions.TrimEntries);
        var body = GearFactory.FindByName(bodySplit[0]);
        if (body == null) { ModInfo.Log.LogWarning($"[SilencedGun] body '{bodySplit[0]}' not found"); return; }
        string statsName = opt.TryGetValue("stats", out var sn) ? sn : defaultStats;
        var statsGear = GearFactory.FindByName(statsName);
        if (statsGear == null) { ModInfo.Log.LogWarning($"[SilencedGun] weapon '{statsName}' not found"); return; }
        var sc = GearFactory.GetComps(statsGear.GearJSON);
        if (!sc.TryGetValue((int)eGearComponent.Category, out var statsCat)) return;

        // Keys from the gun's name: removing one variant doesn't move the others' IDs.
        string key = keyPrefix + new string(nameSplit[0].Where(char.IsLetterOrDigit).ToArray());
        string type = opt.TryGetValue("type", out var ty) ? ty : defaultType;
        uint catId = silent
            ? GearFactory.CloneCategory(statsCat, key, "DRED SILENZIO",
                Lang.T("Silenced. Wakes enemies only within 10 m; farther ones only stir.", "С глушителем. Будит только в 10 м, дальше враги лишь тревожатся."))
            : GearFactory.CloneCategory(statsCat, key, string.Join(" ", nameSplit[0].Split(' ').Take(2)), null);
        if (silent) { _categories.Add(catId); _mainCategories.Add(catId); }
        GearFactory.OwnArchetypes(catId, key, type);
        if (GearFactory.GetComps(body.GearJSON).TryGetValue((int)eGearComponent.Category, out var bodyCat) && bodyCat != statsCat)
            CopyGrip(bodyCat, catId);

        uint? fireMode = sc.TryGetValue((int)eGearComponent.FireMode, out var fm) ? fm : null;
        int burst = opt.TryGetValue("burst", out var b) && int.TryParse(b, out var bn) ? bn : 0;
        if (burst > 0 && MakeBurst(catId, key, burst, type, opt)) fireMode = (uint)eWeaponFireMode.Burst;

        var comps = new Dictionary<int, uint?>
        {
            [(int)eGearComponent.Category] = catId,
            [(int)eGearComponent.FireMode] = fireMode,
            [(int)eGearComponent.BaseItem] = sc.TryGetValue((int)eGearComponent.BaseItem, out var bi) ? bi : null,
        };
        if (sound.HasValue) comps[(int)eGearComponent.AudioSetting] = sound;
        if (bodySplit.Length > 1)
            foreach (var kv in GearFactory.ParseParts(bodySplit[1], "SilencedGun")) comps[kv.Key] = kv.Value;
        GearFactory.AddGear(body, key + "_Gun", nameSplit[0], comps);
    }

    /// <summary>The category's burst archetype: a copy of its auto (or semi) one with the gun's burst numbers.</summary>
    private static bool MakeBurst(uint catId, string key, int shots, string type, Dictionary<string, string> opt)
    {
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(catId);
        if (cat == null) return false;
        uint src = cat.AutoArchetype != 0 ? cat.AutoArchetype : cat.SemiArchetype != 0 ? cat.SemiArchetype : cat.BurstArchetype;
        if (src == 0) return false;
        uint id = GearFactory.CloneArchetype(src, key + "_Arch_MainBurst");
        var a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
        if (a == null) return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float F(string k, float def) => opt.TryGetValue(k, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f) ? f : def;
        string was = $"damage {a.Damage:0.##} ×{a.PrecisionDamageMulti:0.##} head, falloff {a.DamageFalloff.x:0}-{a.DamageFalloff.y:0} m, shot delay {a.ShotDelay:0.###} s, clip {a.DefaultClipSize}, round cost {a.CostOfBullet:0.###}, spread {a.HipFireSpread:0.##}/{a.AimSpread:0.##}";
        a.FireMode = eWeaponFireMode.Burst;
        a.BurstShotCount = Math.Max(2, shots);
        a.BurstDelay = Math.Max(0.05f, F("delay", 0.25f));
        a.Damage = F("damage", a.Damage);
        a.PrecisionDamageMulti = F("precision", a.PrecisionDamageMulti);
        if (opt.TryGetValue("falloff", out var fo))
        {
            var p = fo.Split('-', 2);
            if (p.Length == 2 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out var f0) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var f1))
                a.DamageFalloff = new Vector2(f0, f1);
        }
        // ammo=<gun>: as many rounds as that gun carries (cost of a round in the reserve) and its magazine size.
        if (opt.TryGetValue("ammo", out var ammoFrom) && AmmoDonor(ammoFrom) is { } donor)
        {
            a.CostOfBullet = donor.CostOfBullet;
            a.DefaultClipSize = donor.DefaultClipSize;
        }
        if (opt.TryGetValue("clip", out var cl) && int.TryParse(cl, out var clip) && clip > 0) a.DefaultClipSize = clip;
        float spread = F("spread", 1f);
        a.HipFireSpread *= spread; a.AimSpread *= spread;
        if (!string.IsNullOrEmpty(type)) a.PublicName = GearFactory.Text(type);
        cat.BurstArchetype = id;
        ModInfo.Log.LogInfo($"[SilencedGun] {key}: burst ×{a.BurstShotCount} — damage {a.Damage:0.##} ×{a.PrecisionDamageMulti:0.##} head, falloff {a.DamageFalloff.x:0}-{a.DamageFalloff.y:0} m, burst pause {a.BurstDelay:0.##} s, clip {a.DefaultClipSize}, round cost {a.CostOfBullet:0.###}, spread {a.HipFireSpread:0.##}/{a.AimSpread:0.##} (donor: {was})");
        return true;
    }

    /// <summary>The main archetype of a gun (its own fire mode) — for its ammo numbers.</summary>
    private static ArchetypeDataBlock AmmoDonor(string name)
    {
        var g = GearFactory.FindByName(name);
        if (g == null || !GearFactory.GetComps(g.GearJSON).TryGetValue((int)eGearComponent.Category, out var c)) { ModInfo.Log.LogWarning($"[SilencedGun] ammo donor '{name}' not found"); return null; }
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(c);
        if (cat == null) return null;
        uint id = cat.SemiArchetype != 0 ? cat.SemiArchetype : cat.AutoArchetype != 0 ? cat.AutoArchetype : cat.BurstArchetype;
        var a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
        if (a != null) ModInfo.Log.LogInfo($"[SilencedGun] ammo like '{name}': clip {a.DefaultClipSize}, round cost {a.CostOfBullet:0.###}");
        return a;
    }

    // ---------- weak flashlights ----------

    private static readonly HashSet<uint> _mainCategories = new();
    private static readonly Dictionary<(uint, int), uint> _weakLights = new();

    /// <summary>Our guns light the way with a weaker copy of their flashlight part's settings.</summary>
    [HarmonyPatch(typeof(PlayerInventoryBase), nameof(PlayerInventoryBase.OnItemEquippableFlashlightWielded))]
    private static class Patch_Flashlight
    {
        private static void Prefix(ItemEquippable wieldedItem, GearPartFlashlight flashlight)
        {
            try
            {
                if (flashlight == null || wieldedItem == null || _categories.Count == 0) return;
                uint cat = wieldedItem.GearIDRange?.GetCompID(eGearComponent.Category) ?? 0;
                if (!_categories.Contains(cat)) return;
                float k = Mathf.Clamp(_mainCategories.Contains(cat) ? _mainLight.Value : _light.Value, 0.05f, 1f);
                if (k >= 0.999f) return;
                flashlight.m_settingsID = WeakLight(flashlight.m_settingsID, k);
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] flashlight: {e.Message}"); }
        }
    }

    private static uint WeakLight(uint src, float k)
    {
        int pct = Mathf.RoundToInt(k * 100f);
        if (_weakLights.TryGetValue((src, pct), out var id)) return id;
        var s = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(src);
        if (s == null) return src;
        string key = $"GtfoPunk_WeakLight_{src}_{pct}";
        if (GameDataBlockBase<FlashlightSettingsDataBlock>.HasBlock(key)) id = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlockID(key);
        else
        {
            var w = GearFactory.CloneBlock(s);
            w.persistentID = GearFactory.StableId<FlashlightSettingsDataBlock>(key);
            w.name = key;
            w.intensity = s.intensity * k;
            w.range = s.range * Mathf.Sqrt(k);
            GameDataBlockBase<FlashlightSettingsDataBlock>.AddBlock(w);
            id = w.persistentID;
            ModInfo.Log.LogInfo($"[SilencedGun] weak flashlight {src} → {id}: intensity {s.intensity:0.##} → {w.intensity:0.##}, range {s.range:0.#} → {w.range:0.#}");
        }
        _weakLights[(src, pct)] = id;
        _weakLights[(id, pct)] = id; // the part keeps our ID once set
        return id;
    }

    /// <summary>Hand pose and which part the left hand holds, from another category.</summary>
    private static void CopyGrip(uint fromCat, uint toCat)
    {
        var src = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(fromCat);
        var dst = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(toCat);
        if (src == null || dst == null) return;
        dst.FPSArmPoseName = src.FPSArmPoseName;
        dst.PartAlignPriority = src.PartAlignPriority;
        dst.ThirdPersonFullbodyMovement = src.ThirdPersonFullbodyMovement;
    }

    private static ConfigEntry<float> _damage;
    private static ConfigEntry<string> _archName;

    /// <summary>The cloned category still points at Arid 5's archetypes: give it its own copies, named and with our damage.</summary>
    private static void OwnArchetypes(uint catId)
    {
        GearFactory.OwnArchetypes(catId, "GtfoPunk_SilencedGun", _archName.Value, (mode, a) =>
        {
            if (_damage.Value <= 0f) return;
            ModInfo.Log.LogInfo($"[SilencedGun] {mode} archetype {a.persistentID}: damage {a.Damage:0.#} → {_damage.Value:0.#} (precision ×{a.PrecisionDamageMulti:0.##})");
            a.Damage = _damage.Value;
        });
    }

    private static bool IsOurs(BulletWeapon w)
    {
        if (w == null || _categories.Count == 0) return false;
        try { var g = w.GearIDRange; return g != null && _categories.Contains(g.GetCompID(eGearComponent.Category)); }
        catch { return false; }
    }

    // Own shot (on the shooter) and someone else's shot (on the host — the "synced" copy of the weapon).
    [HarmonyPatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private static class Patch_Fire { private static void Postfix(BulletWeapon __instance) => OnFire(__instance); }

    [HarmonyPatch(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire))]
    private static class Patch_FireSynced { private static void Postfix(BulletWeaponSynced __instance) => OnFire(__instance); }

    private static void OnFire(BulletWeapon w)
    {
        if (!IsOurs(w)) return;
        var owner = w.Owner;
        if (owner == null) return;
        owner.m_noise = Agent.NoiseType.Silent; // the stock "loud shot" isn't needed
        if (!SNet.IsMaster) return;
        if (_cooldown.TryGetValue(owner.Pointer, out var t) && Time.time < t) return;
        _cooldown[owner.Pointer] = Time.time + 0.4f;
        try { MakeNoise(owner); } catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] {e.Message}"); }
    }

    private static void MakeNoise(PlayerAgent owner)
    {
        float hard = _hard.Value, soft = Math.Max(_soft.Value, hard);
        if (soft <= 0f) return;
        var maker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>();
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(owner.Position, soft, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var limb = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>();
            var enemy = limb?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            // Not heard through walls and closed doors.
            if (Physics.Linecast(owner.EyePosition, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
            var node = enemy.CourseNode;
            if (node == null) continue;
            if (node.m_zone.LocalIndex != GameData.eLocalZoneIndex.Zone_0 && node.m_zone.m_sourceGate != null && !node.m_zone.m_sourceGate.HasBeenOpenedDuringPlay) continue;

            bool wake = Vector3.Distance(owner.EyePosition, enemy.EyePosition) <= hard;
            NoiseManager.MakeNoise(new NM_NoiseData
            {
                noiseMaker = maker,
                position = enemy.Position,
                node = node,
                radiusMin = 0.01f,
                radiusMax = 1f,
                yScale = 1f,
                type = wake ? NM_NoiseType.InstaDetect : (_softPulse.Value ? NM_NoiseType.PulseOnly : NM_NoiseType.Detectable),
                includeToNeightbourAreas = false,
                raycastFirstNode = false,
            });
        }
    }
}
