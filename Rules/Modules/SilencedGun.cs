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
/// The kit's silenced guns, one config section each with the same keys as every gun ([Weapons.&lt;Gun&gt;], see GunConfig):
/// <list type="bullet">
/// <item>DRED GLUKHAR S6 (was DRED SILENZIO GP6-S) — pistol: Bataldo 3RB body with a Mastaba top, Shelling Arid 5's stats,
/// 15 damage (a headshot kills a striker or a shooter, a body shot doesn't), S49's sound.</item>
/// <item>DRED TIKHON-A 20S — rifle: Pres MOD 556 front, Receiver_Rifle_3, Köning PR 11's stock, Sight_23, the short 556
/// magazine; Van Auken LTC5's light fast burst of 3, 24 + 120 rounds, 2.4 a round — three in the head kill a striker.</item>
/// </list>
/// A shot wakes sleepers only within HardRadius (10 m), further out, up to SoftRadius, it only disturbs them (pulse),
/// beyond that — silence; not heard through walls. The host computes the noise, so everyone needs the mod.
/// The optional guns live in GtfoPunk Weapons.
/// </summary>
public class SilencedGun : ModuleBase
{
    public override string Id => "SilencedGun";
    public override string Section => "Weapons.Silent";
    public override IReadOnlyList<string> OldSections => new[] { "SilencedGun" };
    public override string Description => "The kit's silenced guns: pistol DRED GLUKHAR S6 and rifle DRED TIKHON-A 20S ([Weapons.Glukhar], [Weapons.Tikhon]). A shot wakes enemies only up close. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<float> _hard, _soft;
    private static ConfigEntry<bool> _softPulse;
    private static GunConfig _pistol, _rifle;

    private const string Old = "SilencedGun";
    // 4 (ItemFPSSettings) — hand animations: taken from Mastaba, otherwise the hand inserting rounds disappears on reload.
    private static readonly GunConfig.Defaults PistolDefaults = new()
    {
        Name = "DRED GLUKHAR S6", Type = "Silenced Pistol", Silent = true, Body = "Bataldo 3RB", Parts = "12=Mastaba R66, 4=Mastaba R66",
        Stats = "Shelling Arid 5", SoundFrom = "Shelling S49", GripFrom = "Mastaba R66", Damage = "15",
    };
    /// <summary>0.9.38: a light, fast SMG burst of 3 (LTC5's rate and recoil) — the 2-round burst kicked, a striker flinched
    /// after the first round and the second missed. 24 in a magazine, 120 in the reserve (five more magazines).</summary>
    private static readonly GunConfig.Defaults RifleDefaults = new()
    {
        Name = "DRED TIKHON-A 20S", Type = "Silenced Rifle", Silent = true, Body = "Drekker Pres MOD 556", Parts = "12=14, 16=13, 19=11, 21=45, 23=20, 25=3",
        Stats = "Van Auken LTC5", SoundFrom = "Raptus Treffen 2", FireMode = "Burst", Burst = "3", BurstPause = "0.3", Damage = "2.4", HeadMultiplier = "1",
        FalloffFrom = "25", FalloffTo = "50", Magazine = "24", Reserve = "120",
    };
    private const string PistolLineDefault = "DRED GLUKHAR S6: Bataldo 3RB + 12=Mastaba R66, 4=Mastaba R66";
    private static readonly string[] RifleLineDefaults =
    {
        "DRED TIKHON-A 20S: Drekker Pres MOD 556 + 12=14, 16=13, 19=11, 21=45, 23=20, 25=3 | stats=Drekker Pres MOD 556, type=Silenced Rifle, mode=burst, burst=2, damage=3.6, precision=1, falloff=30-60, delay=0.3, rate=TR22 Hanaway, clip=20, reserve=100",
    };

    protected override void OnBindConfig()
    {
        _hard = Bind("HardRadius", 10f, "Radius (m) within which a shot wakes enemies.");
        _soft = Bind("SoftRadius", 25f, "Radius (m) within which a shot only disturbs (pulse) without waking. 0 — don't disturb.");
        _softPulse = Bind("SoftIsPulseOnly", true, "true — distant enemies only pulse; false — they get regular noise (may wake up after repeated shots).");

        // Up to 0.9.37 the pistol's settings were flat keys of [SilencedGun].
        _pistol = GunConfig.Bind(Config, "Weapons.Glukhar", PistolDefaults, new()
        {
            ["Stats"] = new[] { (Old, "StatsFrom", "Shelling Arid 5") },
            ["SoundFrom"] = new[] { (Old, "SoundFrom", "Shelling S49") },
            ["GripFrom"] = new[] { (Old, "PoseFrom", "Mastaba R66") },
            ["Type"] = new[] { (Old, "WeaponType", "Silenced Pistol") },
            ["Damage"] = new[] { (Old, "Damage", "15") },
        });
        var line = ConfigMigration.TakeOrphan(Config, Old, "SilencedGuns");
        if (!string.IsNullOrWhiteSpace(line))
        {
            foreach (var old in new[] { "AM DRED GP1-S:", "DRED SILENZIO GP7-S:", "DRED SILENZIO GP6-S:" }) line = line.Replace(old, "DRED GLUKHAR S6:");
            var first = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (first != null && first != PistolLineDefault && !first.StartsWith("DRED GLUKHAR S6: Bataldo 3RB + 12=Mastaba R66"))
            {
                var spec = GunSpec.Parse(first);
                _pistol.Name.Value = spec.Name; _pistol.Body.Value = spec.Body;
                _pistol.Parts.Value = string.Join(", ", spec.Parts.Select(p => $"{p.Comp}={p.Value}"));
            }
        }

        _rifle = GunConfig.Bind(Config, "Weapons.Tikhon", RifleDefaults, new() { ["SoundFrom"] = new[] { (Old, "RifleSoundFrom", "Raptus Treffen 2") } });
        var rifle = ConfigMigration.TakeOrphan(Config, Old, "Rifle");
        if (!string.IsNullOrWhiteSpace(rifle) && !RifleLineDefaults.Contains(rifle.Trim()))
        {
            var d = GunConfig.FromSpec(GunSpec.Parse(rifle), true);
            ModInfo.Log.LogInfo($"[SilencedGun] your [SilencedGun] Rifle line → [Weapons.Tikhon]");
            _rifle.Name.Value = d.Name; _rifle.Body.Value = d.Body; _rifle.Parts.Value = d.Parts; _rifle.Stats.Value = d.Stats; _rifle.RateFrom.Value = d.RateFrom;
            _rifle.FireMode.Value = d.FireMode; _rifle.Burst.Value = d.Burst; _rifle.BurstPause.Value = d.BurstPause; _rifle.Damage.Value = d.Damage;
            _rifle.HeadMultiplier.Value = d.HeadMultiplier; _rifle.FalloffFrom.Value = d.FalloffFrom; _rifle.FalloffTo.Value = d.FalloffTo;
            _rifle.Magazine.Value = d.Magazine; _rifle.Reserve.Value = d.Reserve; _rifle.Type.Value = d.Type;
        }
    }

    private static readonly HashSet<uint> _categories = new();
    private static readonly Dictionary<IntPtr, float> _cooldown = new();

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnLevelExit += () => _cooldown.Clear();
    }

    private static void Inject()
    {
        // The pistol keeps its gear keys (and so its IDs) from before: category GtfoPunk_SilencedGun (4200), gear _1.
        Build(_pistol, "Shelling Arid 5", "Silenced Pistol", "GtfoPunk_SilencedGun", "GtfoPunk_SilencedGun_1");
        Build(_rifle, "Drekker Pres MOD 556", "Silenced Rifle", null, null);
    }

    private static void Build(GunConfig g, string defStats, string defType, string catKey, string gearKey)
    {
        if (g == null || !g.Enabled.Value || string.IsNullOrWhiteSpace(g.Name.Value)) return;
        try
        {
            var spec = g.ToSpec();
            if (g.Silent.Value) spec.Opt["desc"] = Lang.T("Silenced. Wakes enemies only within 10 m; farther ones only stir.", "С глушителем. Будит только в 10 м, дальше враги лишь тревожатся.");
            uint cat = GunBuilder.AddGun(spec, "GtfoPunk_Silent_", defStats, defType, out _, catKey, gearKey);
            if (cat != 0 && g.Silent.Value) _categories.Add(cat);
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] [{g.Section}]: {e.Message}"); }
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
