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
/// <item>DRED TIKHON-A 24S — rifle: Pres MOD 556 front, Receiver_Rifle_3, Köning PR 11's stock, Sight_23, the short 556
/// magazine; Van Auken LTC5's light fast burst of 3, 24 + 120 rounds, 2.4 a round — three in the head kill a striker.</item>
/// </list>
/// Each gun's shot noise is its own (Silent, WakeRadius, StirRadius, StirWakes, ThroughWalls — see GunNoise): the pistol
/// wakes within 8 m and stirs to 20 m, the rifle 10 / 25 m. The host computes the noise, so everyone needs the mod.
/// The optional guns live in GtfoPunk Weapons.
/// </summary>
public class SilencedGun : ModuleBase
{
    public override string Id => "SilencedGun";
    public override string Section => "Weapons";
    public override IReadOnlyList<string> OldSections => new[] { "Weapons.Silent", "SilencedGun" };
    public override string Description => "The kit's silenced guns: pistol DRED GLUKHAR S6 and rifle DRED TIKHON-A 24S ([Weapons.Glukhar], [Weapons.Tikhon], each with its shot noise). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static GunConfig _pistol, _rifle;

    private const string Old = "SilencedGun";
    // 4 (ItemFPSSettings) — hand animations: taken from Mastaba, otherwise the hand inserting rounds disappears on reload.
    private static readonly GunConfig.Defaults PistolDefaults = new()
    {
        Name = "DRED GLUKHAR S6", Type = "Silenced Pistol", Silent = true, WakeRadius = 8f, StirRadius = 20f, Body = "Bataldo 3RB", Parts = "12=Mastaba R66, 4=Mastaba R66",
        Stats = "Shelling Arid 5", SoundFrom = "Shelling S49", GripFrom = "Mastaba R66", Damage = "15",
    };
    /// <summary>0.9.38: a light, fast SMG burst of 3 (LTC5's rate and recoil) — the 2-round burst kicked, a striker flinched
    /// after the first round and the second missed. 24 in a magazine, 120 in the reserve (five more magazines).</summary>
    private static readonly GunConfig.Defaults RifleDefaults = new()
    {
        Name = "DRED TIKHON-A 24S", Type = "Silenced Rifle", Silent = true, WakeRadius = 10f, StirRadius = 25f, Body = "Drekker Pres MOD 556", Parts = "12=14, 16=13, 19=11, 21=45, 23=20, 25=3",
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
        _cfg = Config;
        // Up to 0.9.38 the radii were shared ([Weapons.Silent] / [SilencedGun]): a changed value goes to both guns.
        float? hard = OldFloat("HardRadius", 10f), soft = OldFloat("SoftRadius", 25f);
        bool? stirWakes = OldBool("SoftIsPulseOnly", true) is bool pulse ? !pulse : null;

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
        if (_rifle.Name.Value == "DRED TIKHON-A 20S") _rifle.Name.Value = RifleDefaults.Name; // 24 rounds since 0.9.38
        foreach (var g in new[] { _pistol, _rifle })
        {
            if (hard.HasValue) g.WakeRadius.Value = hard.Value;
            if (soft.HasValue) g.StirRadius.Value = soft.Value;
            if (stirWakes.HasValue) g.StirWakes.Value = stirWakes.Value;
        }
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

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        GunNoise.Install();
    }

    private static float? OldFloat(string key, float def)
    {
        foreach (var sec in new[] { "Weapons.Silent", Old })
        {
            var v = ConfigMigration.TakeOrphan(_cfg, sec, key);
            if (v != null && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && Math.Abs(f - def) > 0.001f) return f;
        }
        return null;
    }

    private static bool? OldBool(string key, bool def)
    {
        foreach (var sec in new[] { "Weapons.Silent", Old })
        {
            var v = ConfigMigration.TakeOrphan(_cfg, sec, key);
            if (v != null && bool.TryParse(v, out var b) && b != def) return b;
        }
        return null;
    }

    private static ConfigFile _cfg;

    private static void Inject()
    {
        // The pistol keeps its gear keys (and so its IDs) from before: category GtfoPunk_SilencedGun (4200), gear _1.
        Build(_pistol, "Shelling Arid 5", "Silenced Pistol", "GtfoPunk_SilencedGun", "GtfoPunk_SilencedGun_1");
        // The rifle keeps the gear key it had as "DRED TIKHON-A 20S" (so its ID) whatever its name.
        Build(_rifle, "Drekker Pres MOD 556", "Silenced Rifle", "GtfoPunk_Silent_DREDTIKHONA20S", "GtfoPunk_Silent_DREDTIKHONA20S_Gun");
    }

    private static void Build(GunConfig g, string defStats, string defType, string catKey, string gearKey)
    {
        if (g == null || !g.Enabled.Value || string.IsNullOrWhiteSpace(g.Name.Value)) return;
        try
        {
            var spec = g.ToSpec();
            if (g.Silent.Value) spec.Opt["desc"] = Lang.T("Silenced. Wakes enemies only within 10 m; farther ones only stir.", "С глушителем. Будит только в 10 м, дальше враги лишь тревожатся.");
            uint cat = GunBuilder.AddGun(spec, "GtfoPunk_Silent_", defStats, defType, out _, catKey, gearKey);
            GunNoise.Set(cat, g.Noise());
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] [{g.Section}]: {e.Message}"); }
    }
}
