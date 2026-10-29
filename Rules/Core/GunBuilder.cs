using System.Globalization;
using GameData;
using Gear;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// A gun from a line 'Name: Body + part=ID, … | option=value, …':
/// <list type="bullet">
/// <item>Body — the gun whose parts (and hand animations, sound, attachments) are the base; part=ID replaces a part
/// (12 front, 16 receiver, 19 stock, 21 sight, 23 magazine, 25 flashlight; IDs in GtfoPunk.GearParts.txt from GtfoPunk Rules,
/// or pick them in the garage).</item>
/// <item>The gun gets a category of its own: stats from the donor (stats=), hand grip from the body, and its own copies
/// of the donor's semi / burst / auto archetypes.</item>
/// <item>Options change the default fire mode's archetype: mode (semi, burst, auto), burst (shots; legacy: also makes burst
/// the mode), damage, precision (head ×), falloff (from-to, m), shotdelay (s between shots), delay (pause between bursts, s),
/// clip, cost (a round's share of the reserve), reserve (rounds the reserve holds — sets cost), rate=&lt;gun&gt; (that gun's shot delay),
/// hip / aim (spread), spread (× both), ammo=&lt;gun&gt; (that gun's clip and cost), sound=&lt;gun&gt; (that gun's shot sound),
/// type (weapon type shown above the name).</item>
/// </list>
/// </summary>
public class GunSpec
{
    public string Name = "", Body = "";
    public List<(int Comp, string Value)> Parts = new();
    public Dictionary<string, string> Opt = new(StringComparer.OrdinalIgnoreCase);

    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static GunSpec Parse(string line)
    {
        var s = new GunSpec();
        var nameSplit = line.Split(':', 2, StringSplitOptions.TrimEntries);
        string rest = nameSplit.Length == 2 ? nameSplit[1] : nameSplit[0];
        if (nameSplit.Length == 2) s.Name = nameSplit[0];
        var optSplit = rest.Split('|', 2, StringSplitOptions.TrimEntries);
        if (optSplit.Length > 1)
            foreach (var o in optSplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = o.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2) s.Opt[kv[0]] = kv[1];
            }
        var bodySplit = optSplit[0].Split('+', 2, StringSplitOptions.TrimEntries);
        s.Body = bodySplit[0];
        if (bodySplit.Length > 1)
            foreach (var item in bodySplit[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = item.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && int.TryParse(kv[0], out var c)) s.Parts.Add((c, kv[1]));
            }
        return s;
    }

    /// <summary>The line without the name: 'Body + parts | options'.</summary>
    public string Format()
    {
        string parts = Parts.Count > 0 ? " + " + string.Join(", ", Parts.Select(p => $"{p.Comp}={p.Value}")) : "";
        string opts = Opt.Count > 0 ? " | " + string.Join(", ", Opt.Select(kv => $"{kv.Key}={kv.Value}")) : "";
        return Body + parts + opts;
    }

    public bool TryFloat(string key, out float v)
    {
        v = 0f;
        return Opt.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, Ci, out v);
    }

    public static string F(float v) => v.ToString("0.###", Ci);
}

public static class GunBuilder
{
    /// <summary>Builds the gun; returns its category ID (0 — failed). <paramref name="gear"/> — the new gear block.</summary>
    public static uint AddGun(GunSpec spec, string keyPrefix, string defaultStats, string defaultType, out PlayerOfflineGearDataBlock gear)
    {
        gear = null;
        var body = GearFactory.FindByName(spec.Body);
        if (body == null) { ModInfo.Log.LogWarning($"[Guns] body '{spec.Body}' not found"); return 0; }
        string statsName = spec.Opt.TryGetValue("stats", out var sn) ? sn : defaultStats;
        var statsGear = GearFactory.FindByName(statsName);
        if (statsGear == null) { ModInfo.Log.LogWarning($"[Guns] stats donor '{statsName}' not found — the body's stats"); statsGear = body; }
        var sc = GearFactory.GetComps(statsGear.GearJSON);
        if (!sc.TryGetValue((int)eGearComponent.Category, out var statsCat)) return 0;

        // Keys from the gun's name: removing one gun doesn't move the others' IDs.
        string key = keyPrefix + new string(spec.Name.Where(char.IsLetterOrDigit).ToArray());
        string type = spec.Opt.TryGetValue("type", out var ty) ? ty : defaultType;
        uint catId = GearFactory.CloneCategory(statsCat, key, string.Join(" ", spec.Name.Split(' ').Take(2)), null);
        if (GearFactory.GetComps(body.GearJSON).TryGetValue((int)eGearComponent.Category, out var bodyCat) && bodyCat != statsCat)
            CopyGrip(bodyCat, catId);
        // Lines written before the garage had modes (burst=N, no mode=): their burst was made from the donor's auto.
        bool legacyBurst = !spec.Opt.ContainsKey("mode") && spec.Opt.ContainsKey("burst");
        BuildArchetypes(catId, statsCat, key, type, legacyBurst);
        var mode = ApplyOptions(catId, spec, out var log);
        ModInfo.Log.LogInfo($"[Guns] {spec.Name}: stats of '{GearFactory.NameOf(statsGear)}', {log}");

        var comps = new Dictionary<int, uint?>
        {
            [(int)eGearComponent.Category] = catId,
            [(int)eGearComponent.FireMode] = (uint)mode,
            [(int)eGearComponent.BaseItem] = sc.TryGetValue((int)eGearComponent.BaseItem, out var bi) ? bi : null,
        };
        if (spec.Opt.TryGetValue("sound", out var soundFrom))
        {
            var sg = GearFactory.FindByName(soundFrom);
            if (sg != null && GearFactory.GetComps(sg.GearJSON).TryGetValue((int)eGearComponent.AudioSetting, out var snd)) comps[(int)eGearComponent.AudioSetting] = snd;
            else ModInfo.Log.LogWarning($"[Guns] sound '{soundFrom}' not found — the body's sound");
        }
        if (spec.Parts.Count > 0)
            foreach (var kv in GearFactory.ParseParts(string.Join(", ", spec.Parts.Select(p => $"{p.Comp}={p.Value}")), "Garage")) comps[kv.Key] = kv.Value;
        gear = GearFactory.AddGear(body, key + "_Gun", spec.Name, comps);
        return catId;
    }

    /// <summary>
    /// The category's own semi / burst / auto archetypes, copied from the donor category's (a mode the donor lacks is
    /// made from its nearest one). Called again when the garage changes the donor: the copies are refilled in place.
    /// </summary>
    public static void BuildArchetypes(uint catId, uint donorCatId, string key, string type, bool burstFromAuto = false)
    {
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(catId);
        var donor = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(donorCatId);
        if (cat == null || donor == null) return;
        uint semi = donor.SemiArchetype, burst = donor.BurstArchetype, auto = donor.AutoArchetype;
        uint any = auto != 0 ? auto : semi != 0 ? semi : burst;
        uint Make(uint src, eWeaponFireMode mode)
        {
            bool own = src != 0;
            if (!own) src = mode == eWeaponFireMode.Semi ? (semi != 0 ? semi : any) : any;
            var from = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(src);
            if (from == null) return 0;
            string k = key + "_Arch_" + mode;
            ArchetypeDataBlock a;
            if (GameDataBlockBase<ArchetypeDataBlock>.HasBlock(k))
            {
                a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(k);
                GearFactory.CopyInto(from, a, "persistentID", "name");
            }
            else a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(GearFactory.CloneArchetype(src, k));
            if (a == null) return 0;
            a.FireMode = mode;
            if (mode == eWeaponFireMode.Burst && !own) { a.BurstShotCount = 3; a.BurstDelay = 0.25f; }
            if (!string.IsNullOrEmpty(type)) a.PublicName = GearFactory.Text(type);
            return a.persistentID;
        }
        cat.SemiArchetype = Make(semi, eWeaponFireMode.Semi);
        cat.BurstArchetype = Make(burstFromAuto ? 0 : burst, eWeaponFireMode.Burst);
        cat.AutoArchetype = Make(auto, eWeaponFireMode.Auto);
        cat.SemiBurstArchetype = donor.SemiBurstArchetype;
    }

    /// <summary>The donor's default fire mode (its gear's fire-mode part).</summary>
    public static eWeaponFireMode DonorMode(PlayerOfflineGearDataBlock donorGear)
        => GearFactory.GetComps(donorGear.GearJSON).TryGetValue((int)eGearComponent.FireMode, out var fm) ? (eWeaponFireMode)fm : eWeaponFireMode.Auto;

    public static ArchetypeDataBlock Archetype(uint catId, eWeaponFireMode mode)
    {
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(catId);
        if (cat == null) return null;
        uint id = mode switch { eWeaponFireMode.Semi => cat.SemiArchetype, eWeaponFireMode.Burst => cat.BurstArchetype, _ => cat.AutoArchetype };
        return id == 0 ? null : GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
    }

    /// <summary>Options into the default mode's archetype; returns that mode.</summary>
    public static eWeaponFireMode ApplyOptions(uint catId, GunSpec spec, out string log)
    {
        eWeaponFireMode mode = eWeaponFireMode.Auto;
        var statsGear = spec.Opt.TryGetValue("stats", out var sn) ? GearFactory.FindByName(sn) : null;
        if (statsGear != null) mode = DonorMode(statsGear);
        if (spec.Opt.TryGetValue("mode", out var m) && Enum.TryParse<eWeaponFireMode>(m, true, out var pm)) mode = pm;
        else if (spec.TryFloat("burst", out var bn) && bn > 0) mode = eWeaponFireMode.Burst; // legacy lines: burst=N makes it the mode
        if (mode == eWeaponFireMode.SemiBurst) mode = eWeaponFireMode.Burst;
        var a = Archetype(catId, mode);
        if (a == null) { log = "no archetype"; return mode; }

        if (spec.Opt.TryGetValue("ammo", out var ammoFrom) && AmmoDonor(ammoFrom) is { } ad) { a.CostOfBullet = ad.CostOfBullet; a.DefaultClipSize = ad.DefaultClipSize; }
        if (spec.TryFloat("burst", out var b) && b >= 2) a.BurstShotCount = (int)b;
        if (spec.TryFloat("delay", out var d)) a.BurstDelay = Math.Max(0.03f, d);
        if (spec.Opt.TryGetValue("rate", out var rateFrom) && MainArchetype(rateFrom) is { } rd) a.ShotDelay = rd.ShotDelay;
        if (spec.TryFloat("shotdelay", out var sd)) a.ShotDelay = Math.Max(0.01f, sd);
        if (spec.TryFloat("damage", out var dm)) a.Damage = Math.Max(0.01f, dm);
        if (spec.TryFloat("precision", out var pr)) a.PrecisionDamageMulti = Math.Max(0f, pr);
        if (spec.Opt.TryGetValue("falloff", out var fo))
        {
            var p = fo.Split('-', 2);
            if (p.Length == 2 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f0)
                && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var f1)) a.DamageFalloff = new Vector2(f0, Math.Max(f0, f1));
        }
        if (spec.TryFloat("clip", out var cl) && cl >= 1) a.DefaultClipSize = (int)cl;
        if (spec.TryFloat("cost", out var co) && co > 0) a.CostOfBullet = co;
        if (spec.TryFloat("reserve", out var rs) && rs >= 1 && MaxCap() > 0) a.CostOfBullet = MaxCap() / rs;
        if (spec.TryFloat("spread", out var sp)) { a.HipFireSpread *= sp; a.AimSpread *= sp; }
        if (spec.TryFloat("hip", out var hs)) a.HipFireSpread = Math.Max(0f, hs);
        if (spec.TryFloat("aim", out var am)) a.AimSpread = Math.Max(0f, am);
        log = $"{mode}" + (mode == eWeaponFireMode.Burst ? $" ×{a.BurstShotCount}" : "") + $" — damage {a.Damage:0.##} ×{a.PrecisionDamageMulti:0.##} head, falloff {a.DamageFalloff.x:0}-{a.DamageFalloff.y:0} m, shot delay {a.ShotDelay:0.###} s, burst pause {a.BurstDelay:0.##} s, clip {a.DefaultClipSize}, round cost {a.CostOfBullet:0.###}, spread {a.HipFireSpread:0.##}/{a.AimSpread:0.##}";
        return mode;
    }

    /// <summary>The main-slot ammo the player carries (in ammo units; a round costs CostOfBullet of them) and a full pack's share.</summary>
    public static float MaxCap() => GameDataBlockBase<PlayerDataBlock>.GetBlock(1u)?.AmmoStandardMaxCap ?? 0;
    public static float PackCap() => GameDataBlockBase<PlayerDataBlock>.GetBlock(1u)?.AmmoStandardResourcePackMaxCap ?? 0;

    /// <summary>A gun's archetype in its own default fire mode.</summary>
    private static ArchetypeDataBlock MainArchetype(string name)
    {
        var g = GearFactory.FindByName(name);
        if (g == null || !GearFactory.GetComps(g.GearJSON).TryGetValue((int)eGearComponent.Category, out var c)) { ModInfo.Log.LogWarning($"[Guns] rate donor '{name}' not found"); return null; }
        var mode = DonorMode(g);
        return Archetype(c, mode == eWeaponFireMode.SemiBurst ? eWeaponFireMode.Burst : mode) ?? AmmoDonor(name);
    }

    /// <summary>The main archetype of a gun — for its ammo numbers.</summary>
    private static ArchetypeDataBlock AmmoDonor(string name)
    {
        var g = GearFactory.FindByName(name);
        if (g == null || !GearFactory.GetComps(g.GearJSON).TryGetValue((int)eGearComponent.Category, out var c)) { ModInfo.Log.LogWarning($"[Guns] ammo donor '{name}' not found"); return null; }
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(c);
        if (cat == null) return null;
        uint id = cat.SemiArchetype != 0 ? cat.SemiArchetype : cat.AutoArchetype != 0 ? cat.AutoArchetype : cat.BurstArchetype;
        return GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
    }

    /// <summary>Hand pose and which part the left hand holds, from another category.</summary>
    public static void CopyGrip(uint fromCat, uint toCat)
    {
        var src = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(fromCat);
        var dst = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(toCat);
        if (src == null || dst == null) return;
        dst.FPSArmPoseName = src.FPSArmPoseName;
        dst.PartAlignPriority = src.PartAlignPriority;
        dst.ThirdPersonFullbodyMovement = src.ThirdPersonFullbodyMovement;
    }
}
