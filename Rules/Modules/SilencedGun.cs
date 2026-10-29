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
/// "DRED GLUKHAR S6" (was DRED SILENZIO GP6-S): silenced pistol from the "garage". Damage (lower, 15 by default) from Shelling Arid 5, sound from S49,
/// body and barrel assembled from parts of different guns (option Guns). A shot wakes sleepers only within HardRadius (10 m),
/// further out, up to SoftRadius, it only disturbs them (pulse), beyond that — silence. Not heard through walls.
/// The host computes the noise, so everyone needs the mod.
/// "DRED TIKHON-A 20S" (option Rifle, 0.9.37): a silenced main-slot rifle, part of the game's kit; the optional silenced
/// guns (DRED ASSASSIN SMG-SA / AR-S) live in GtfoPunk Weapons.
/// </summary>
public class SilencedGun : ModuleBase
{
    public override string Id => "SilencedGun";
    public override string Description => "Silenced pistol DRED GLUKHAR S6 and rifle DRED TIKHON-A 20S (2-round burst, 20 + 100). Pistol: Arid 5 body (damage 15 — headshots kill), S49 sound, 3RB body + Mastaba top. Only wakes enemies up close. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<string> _stats, _sound, _guns, _pose, _rifle, _rifleSound;
    private static ConfigEntry<float> _hard, _soft;
    private static ConfigEntry<bool> _softPulse;

    protected override void OnBindConfig()
    {
        _stats = Bind("StatsFrom", "Shelling Arid 5", "Weapon to take damage, magazine and rate of fire from (category).");
        _sound = Bind("SoundFrom", "Shelling S49", "Weapon to take the shot sound from.");
        _pose = Bind("PoseFrom", "Mastaba R66", "Weapon to take the grip and hand pose from (pose and which part the left hand holds). Must match the gun's \"top\", otherwise the hand disappears on reload.");
        _guns = Bind("SilencedGuns", NewDefault,
            "Silenced guns separated by ';': 'Name: Body + part=from, …'. Body — the weapon whose parts are the base; 12 — barrel/top. Parts and IDs are in GtfoPunk.GearList.txt.");
        _rifle = Bind("Rifle", RifleDefault,
            "The silenced rifle: 'Name: Body + part=ID, … | option=value, …' (the GtfoPunk Weapons line format: stats, type, mode, burst, delay, shotdelay, rate=<gun>, damage, precision, falloff, clip, cost, reserve, hip, aim, sound). Empty — none.");
        _rifleSound = Bind("RifleSoundFrom", "Raptus Treffen 2", "The silenced rifle's quiet shot sound: taken from this gun.");
        _hard = Bind("HardRadius", 10f, "Radius (m) within which a shot wakes enemies.");
        _damage = Bind("Damage", 15f, "Damage per shot. 15: a headshot kills a striker or a shooter, a body shot doesn't (Arid 5 itself does far more). 0 — Arid 5's own.");
        _archName = Bind("WeaponType", "Silenced Pistol", "Weapon type shown above the gun's name in the weapon list (the gun's own name is in SilencedGuns).");
        _soft = Bind("SoftRadius", 25f, "Radius (m) within which a shot only disturbs (pulse) without waking. 0 — don't disturb.");
        _softPulse = Bind("SoftIsPulseOnly", true, "true — distant enemies only pulse; false — they get regular noise (may wake up after repeated shots).");
    }

    // 4 (ItemFPSSettings) — hand animations: taken from Mastaba, otherwise the hand inserting rounds disappears on reload.
    private const string NewDefault = "DRED GLUKHAR S6: Bataldo 3RB + 12=Mastaba R66, 4=Mastaba R66";
    /// <summary>DRED TIKHON-A 20S (from GtfoPunk Weapons 0.1.1–0.1.2): Pres MOD 556 front, Receiver_Rifle_3, Köning PR 11's stock,
    /// Sight_23, the short 556 magazine, flashlight B; Hanaway's rate, a 2-round burst, 20 + 100 rounds, 3.6 a round — both
    /// rounds in the head kill a striker, a body burst doesn't. Part of the game: a quiet main gun for everyone.</summary>
    private const string RifleDefault = "DRED TIKHON-A 20S: Drekker Pres MOD 556 + 12=14, 16=13, 19=11, 21=45, 23=20, 25=3 | stats=Drekker Pres MOD 556, type=Silenced Rifle, mode=burst, burst=2, damage=3.6, precision=1, falloff=30-60, delay=0.3, rate=TR22 Hanaway, clip=20, reserve=100";
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
        foreach (var old in new[] { "AM DRED GP1-S:", "DRED SILENZIO GP7-S:", "DRED SILENZIO GP6-S:" }) // renames
            if (_guns.Value.Contains(old)) _guns.Value = _guns.Value.Replace(old, "DRED GLUKHAR S6:");
        // Migrate old default values (temporary — including test variant B: full Mastaba + 3RB stock).
        if (_guns.Value == "DRED GLUKHAR S6: Bataldo 3RB + 12=Mastaba R66" || _guns.Value == TestDefault) _guns.Value = NewDefault;
        var statsGear = GearFactory.FindByName(_stats.Value);
        if (statsGear == null) { ModInfo.Log.LogWarning($"[SilencedGun] weapon '{_stats.Value}' not found — see GtfoPunk.GearList.txt"); return; }
        var sc = GearFactory.GetComps(statsGear.GearJSON);
        if (!sc.TryGetValue((int)eGearComponent.Category, out var statsCat)) return;
        uint catId = GearFactory.CloneCategory(statsCat, "GtfoPunk_SilencedGun", "DRED GLUKHAR",
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
        try { InjectRifle(); } catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] rifle: {e.Message}"); }
    }

    /// <summary>The silenced main-slot rifle — the same gear key GtfoPunk Weapons used, so it keeps its gear ID.</summary>
    private static void InjectRifle()
    {
        if (string.IsNullOrWhiteSpace(_rifle.Value)) return;
        var spec = GunSpec.Parse(_rifle.Value);
        if (spec.Name.Length == 0) { ModInfo.Log.LogWarning($"[SilencedGun] no name in '{_rifle.Value}'"); return; }
        if (!spec.Opt.ContainsKey("sound") && !string.IsNullOrWhiteSpace(_rifleSound.Value)) spec.Opt["sound"] = _rifleSound.Value;
        uint cat = GunBuilder.AddGun(spec, "GtfoPunk_Silent_", "Drekker Pres MOD 556", "Silenced Rifle", out _);
        if (cat != 0) _categories.Add(cat);
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
