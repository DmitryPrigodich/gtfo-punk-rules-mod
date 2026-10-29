using System.Globalization;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// One gun = one config section with the same keys everywhere ([Weapons.&lt;Gun&gt;]): name, look (body, parts), donors
/// (stats, sound, grip, rate, ammo, recoil) and numbers. An empty number keeps the stats donor's value; 'x1.2' multiplies it.
/// Turned into a <see cref="GunSpec"/> for <see cref="GunBuilder"/>.
/// </summary>
public class GunConfig
{
    public string Section;
    public ConfigEntry<bool> Enabled, Silent, StirWakes, ThroughWalls;
    public ConfigEntry<float> WakeRadius, StirRadius;
    public ConfigEntry<string> Name, Type, Body, Parts, Stats, SoundFrom, GripFrom, RateFrom, AmmoFrom, RecoilFrom,
        FireMode, Burst, BurstPause, ShotDelay, Damage, HeadMultiplier, FalloffFrom, FalloffTo, FalloffCurve, Magazine, Reserve, SpreadHip, SpreadAim;

    /// <summary>Default values of a gun's keys (all strings but the two flags; empty — the donor's).</summary>
    public class Defaults
    {
        public bool Enabled = true, Silent, StirWakes, ThroughWalls;
        /// <summary>The gun's own shot noise (m): wakes within WakeRadius, stirs up to StirRadius; 0 — none.</summary>
        public float WakeRadius, StirRadius;
        public string Name = "", Type = "", Body = "", Parts = "", Stats = "", SoundFrom = "", GripFrom = "", RateFrom = "", AmmoFrom = "", RecoilFrom = "",
            FireMode = "", Burst = "", BurstPause = "", ShotDelay = "", Damage = "", HeadMultiplier = "", FalloffFrom = "", FalloffTo = "", FalloffCurve = "Linear",
            Magazine = "", Reserve = "", SpreadHip = "", SpreadAim = "";
    }

    private const string Num = " A number, 'x1.2' (times the stats donor's), or empty — the donor's.";

    /// <summary>Binds the section. <paramref name="old"/>: earlier places of a key ("Key" → old section, old key, old default).</summary>
    public static GunConfig Bind(ConfigFile cfg, string section, Defaults d, Dictionary<string, (string Section, string Key, string OldDefault)[]> old = null)
    {
        ConfigEntry<T> B<T>(string key, T def, string desc)
            => ConfigMigration.Bind(cfg, section, key, def, desc, old != null && old.TryGetValue(key, out var o) ? o : Array.Empty<(string, string, string)>());
        return new GunConfig
        {
            Section = section,
            Enabled = B("Enabled", d.Enabled, "The gun is in the game (after a restart)."),
            Name = B("Name", d.Name, "Name in the weapon list."),
            Type = B("Type", d.Type, "Weapon type shown above the name."),
            Silent = B("Silent", d.Silent, "true — no game shot noise, only this gun's radii below; false — the game's loud shot (plus the radii, if any)."),
            WakeRadius = B("WakeRadius", d.WakeRadius, "A shot wakes sleepers within this distance (m). 0 — never (from this gun's own noise)."),
            StirRadius = B("StirRadius", d.StirRadius, "Sleepers stir (pulse) up to this distance (m). 0 — not at all."),
            StirWakes = B("StirWakes", d.StirWakes, "Stirred enemies may wake after shots in a row (false — they only pulse)."),
            ThroughWalls = B("ThroughWalls", d.ThroughWalls, "The shot is heard through walls and closed doors."),
            Body = B("Body", d.Body, "The gun whose parts, hand animations and attachments are the base (and the slot)."),
            Parts = B("Parts", d.Parts, "Parts on the body: 'type=ID, …' — 12 front, 16 receiver, 19 stock, 21 sight, 23 magazine, 25 flashlight; 'type=<gun name>' — that gun's part, 'type=-' — none."),
            Stats = B("Stats", d.Stats, "The stats donor: rate of fire, recoil, spread, magazine, reload, fire modes. Empty — the body."),
            SoundFrom = B("SoundFrom", d.SoundFrom, "The shot sound: taken from this gun. Empty — the body's."),
            GripFrom = B("GripFrom", d.GripFrom, "The hand grip (pose, which part the left hand holds): from this gun. Empty — the body's."),
            RateFrom = B("RateFrom", d.RateFrom, "The rate of fire (shot delay) of this gun. Empty — the stats donor's (or ShotDelay)."),
            AmmoFrom = B("AmmoFrom", d.AmmoFrom, "Magazine and reserve of this gun. Empty — the stats donor's (or Magazine / Reserve)."),
            RecoilFrom = B("RecoilFrom", d.RecoilFrom, "The recoil of this gun. Empty — the stats donor's."),
            FireMode = B("FireMode", d.FireMode, "Default fire mode: Semi, Burst or Auto. Empty — the stats donor's."),
            Burst = B("Burst", d.Burst, "Shots in a burst." + Num),
            BurstPause = B("BurstPause", d.BurstPause, "Pause between bursts (s)." + Num),
            ShotDelay = B("ShotDelay", d.ShotDelay, "Seconds between shots (inside a burst too)." + Num),
            Damage = B("Damage", d.Damage, "Damage per round." + Num),
            HeadMultiplier = B("HeadMultiplier", d.HeadMultiplier, "The gun's head multiplier (the enemy's own comes on top)." + Num),
            FalloffFrom = B("FalloffFrom", d.FalloffFrom, "Full damage up to this distance (m)." + Num),
            FalloffTo = B("FalloffTo", d.FalloffTo, "Damage gone by this distance (m)." + Num),
            FalloffCurve = B("FalloffCurve", d.FalloffCurve, "How damage falls after FalloffFrom: Linear (the game's straight line to FalloffTo), Square ((1 − t)², drops faster at first), Hyperbola (FalloffFrom / distance — a long tail, FalloffTo unused)."),
            Magazine = B("Magazine", d.Magazine, "Rounds in a magazine." + Num),
            Reserve = B("Reserve", d.Reserve, "Rounds the reserve holds when full." + Num),
            SpreadHip = B("SpreadHip", d.SpreadHip, "Spread from the hip." + Num),
            SpreadAim = B("SpreadAim", d.SpreadAim, "Spread when aiming." + Num),
        };
    }

    public NoiseSpec Noise() => new() { Silent = Silent.Value, Wake = WakeRadius.Value, Stir = StirRadius.Value, StirWakes = StirWakes.Value, ThroughWalls = ThroughWalls.Value };

    /// <summary>The usual silenced gun: wakes within 10 m, stirs up to 25 m, not through walls.</summary>
    public static void Quiet(Defaults d, float wake = 10f, float stir = 25f) { d.Silent = true; d.WakeRadius = wake; d.StirRadius = stir; }

    /// <summary>The section as a builder line.</summary>
    public GunSpec ToSpec()
    {
        var s = GunSpec.Parse($"{Name.Value}: {Body.Value}" + (string.IsNullOrWhiteSpace(Parts.Value) ? "" : " + " + Parts.Value));
        void O(string key, ConfigEntry<string> e) { if (!string.IsNullOrWhiteSpace(e.Value)) s.Opt[key] = e.Value.Trim(); }
        O("type", Type); O("stats", Stats); O("sound", SoundFrom); O("grip", GripFrom); O("rate", RateFrom); O("ammo", AmmoFrom); O("recoil", RecoilFrom);
        if (!string.IsNullOrWhiteSpace(FireMode.Value)) s.Opt["mode"] = FireMode.Value.Trim().ToLowerInvariant();
        O("burst", Burst); O("delay", BurstPause); O("shotdelay", ShotDelay); O("damage", Damage); O("precision", HeadMultiplier);
        O("falloff0", FalloffFrom); O("falloff1", FalloffTo); O("curve", FalloffCurve); O("clip", Magazine); O("reserve", Reserve); O("hip", SpreadHip); O("aim", SpreadAim);
        return s;
    }

    /// <summary>A builder line as section defaults (for older one-line settings and guns the garage saves).</summary>
    public static Defaults FromSpec(GunSpec s, bool silent)
    {
        string O(string k) => s.Opt.TryGetValue(k, out var v) ? v : "";
        var d = new Defaults
        {
            Silent = silent, WakeRadius = silent ? 10f : 0f, StirRadius = silent ? 25f : 0f, Name = s.Name, Type = O("type"), Body = s.Body, Parts = string.Join(", ", s.Parts.Select(p => $"{p.Comp}={p.Value}")),
            Stats = O("stats"), SoundFrom = O("sound"), GripFrom = O("grip"), RateFrom = O("rate"), AmmoFrom = O("ammo"), RecoilFrom = O("recoil"),
            FireMode = O("mode"), Burst = O("burst"), BurstPause = O("delay"), ShotDelay = O("shotdelay"), Damage = O("damage"), HeadMultiplier = O("precision"),
            FalloffFrom = O("falloff0"), FalloffTo = O("falloff1"), FalloffCurve = O("curve") is { Length: > 0 } fc ? fc : "Linear", Magazine = O("clip"), Reserve = O("reserve"), SpreadHip = O("hip"), SpreadAim = O("aim"),
        };
        if (s.Opt.TryGetValue("falloff", out var f) && f.Split('-', 2) is { Length: 2 } p) { d.FalloffFrom = p[0].Trim(); d.FalloffTo = p[1].Trim(); }
        if (s.Opt.TryGetValue("spread", out var sp) && sp.Length > 0) { if (d.SpreadHip == "") d.SpreadHip = "x" + sp; if (d.SpreadAim == "") d.SpreadAim = "x" + sp; }
        // Older lines without a mode: burst=N made the burst the mode.
        if (d.FireMode == "" && d.Burst != "") d.FireMode = "burst";
        if (s.Opt.TryGetValue("cost", out var c) && d.Reserve == "")
        {
            float cap = GunBuilder.MaxCap();
            if (cap > 0 && float.TryParse(c, NumberStyles.Float, CultureInfo.InvariantCulture, out var cost) && cost > 0) d.Reserve = Math.Round(cap / cost).ToString(CultureInfo.InvariantCulture);
        }
        return d;
    }

    /// <summary>A section id from a gun's name: "DRED BULL GOLORAT-2" → "DredBullGolorat2".</summary>
    public static string IdOf(string name)
    {
        var parts = (name ?? "").Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray())).Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant());
        var id = string.Concat(parts);
        return id.Length > 0 ? id : "Gun";
    }
}
