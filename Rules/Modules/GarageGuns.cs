namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Ordinary (loud) guns from the garage, assembled like the silenced ones — the same 'Name: Body + parts | options'
/// format (see SilencedGun.MainGuns): their own rate of fire, burst, damage, falloff, ammo.
/// "DRED BULL GOLORAT-2" — a bullpup rifle, an alternative to the Accrat Golok DA: a hard 3-shot burst up close
/// that falls off quickly with range. Changes gear, so everyone needs the mod.
/// </summary>
public class GarageGuns : ModuleBase
{
    public override string Id => "GarageGuns";
    public override string Description => "New ordinary guns from the garage: DRED BULL GOLORAT-2 (bullpup rifle, short-range burst). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<string> _guns;

    private const string Default =
        "DRED BULL GOLORAT-2: Accrat Golok DA + 12=46, 16=20, 19=17, 21=45 | stats=Accrat Golok DA, type=Bullpup Rifle, burst=3, damage=7, precision=1, falloff=8-18, delay=0.18, spread=1.15";

    protected override void OnBindConfig()
    {
        _guns = Bind("Guns", Default,
            "Guns separated by ';': 'Name: Body + part=ID, … | option=value, …' — options as in SilencedGun.MainGuns (stats, type, burst, damage, precision, falloff, delay, clip, ammo, spread). Empty — none.");
    }

    protected override void OnEnable() => GearFactory.OnInject += Inject;

    private static void Inject()
    {
        foreach (var entry in _guns.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { SilencedGun.AddGun(entry, silent: false, "GtfoPunk_Garage_", "Accrat Golok DA", "Rifle", sound: null); }
            catch (Exception e) { ModInfo.Log.LogWarning($"[GarageGuns] '{entry}': {e.Message}"); }
        }
    }
}
