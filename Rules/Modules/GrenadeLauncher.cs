using GameData;
using Gear;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "DRED KHLOPUSHKA GL-10": a special-slot grenade launcher. Each shot lobs one small contact grenade — the TGL3 sentry's
/// (module Grenades: proximity fuse, mine-like blast) — a bit weaker and smaller: 35 damage at the centre, 3.5 m.
/// A 10-grenade box magazine, 30 in the reserve, semi-auto (0.6 s). The gun's own bullets do nothing.
/// Look: Buckland AF6's front, Omneco's machine-gun receiver, Buckland XDist2's stock and iron sights, TechMan Klust 6's
/// drum, flashlight E. The blast hurts players too — don't fire point blank. Everyone needs the mod (new gear).
/// </summary>
public class GrenadeLauncher : ModuleBase
{
    public override string Id => "GrenadeLauncher";
    public override string Description => "DRED KHLOPUSHKA GL-10: special-slot grenade launcher — lobs small contact grenades (35 damage, 3.5 m), 10 + 30. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<string> _name, _body, _parts, _type;
    private static ConfigEntry<int> _clip, _reserve;
    private static ConfigEntry<float> _delay, _damage, _radius, _speed, _loft;

    protected override void OnBindConfig()
    {
        _name = Bind("Name", "DRED KHLOPUSHKA GL-10", "The launcher's name in the weapon list.");
        _type = Bind("WeaponType", "Grenade Launcher", "Weapon type shown above the name.");
        _body = Bind("Body", "Omneco LRG", "The special-slot gun the launcher is built on (hand grip, reload, slot).");
        _parts = Bind("Parts", "12=12, 16=10, 19=12, 21=43, 23=16, 25=5",
            "Parts on the body: 12 front, 16 receiver, 19 stock, 21 sight, 23 magazine, 25 flashlight ('type=ID', IDs in GtfoPunk.GearParts.txt).");
        _clip = Bind("Magazine", 10, "Grenades in a magazine.");
        _reserve = Bind("Reserve", 30, "Grenades the special-ammo reserve holds when full.");
        _delay = Bind("ShotDelay", 0.6f, "Seconds between shots (semi-auto).");
        _damage = Bind("Damage", 35f, "Grenade damage at the blast centre (fades with distance). The TGL3 sentry's: 40.");
        _radius = Bind("Radius", 3.5f, "Blast radius (m). The TGL3 sentry's: 5.");
        _speed = Bind("Speed", 20f, "Launch speed (m/s) — lower lobs higher.");
        _loft = Bind("Loft", 4f, "Degrees the grenade flies above the aim line (to lob it a little).");
    }

    protected override void OnEnable() => GearFactory.OnInject += Inject;

    private static uint _cat;
    private const string Key = "GtfoPunk_GrenadeLauncher";

    private static void Inject()
    {
        Grenades.LauncherRadius = Math.Max(0.5f, _radius.Value);
        Grenades.LauncherDamage = (int)Math.Max(1f, _damage.Value);
        var body = GearFactory.FindByName(_body.Value);
        if (body == null) { ModInfo.Log.LogWarning($"[GrenadeLauncher] body '{_body.Value}' not found"); return; }
        var bc = GearFactory.GetComps(body.GearJSON);
        if (!bc.TryGetValue((int)eGearComponent.Category, out var bodyCat)) return;
        _cat = GearFactory.CloneCategory(bodyCat, Key, string.Join(" ", _name.Value.Split(' ').Take(2)),
            Lang.T("Lobs small contact grenades. The blast hurts players too.", "Стреляет маленькими гранатами на контакт. Взрыв ранит и своих."));

        float max = GameDataBlockBase<PlayerDataBlock>.GetBlock(1u)?.AmmoSpecialMaxCap ?? 0;
        float cost = max > 0 && _reserve.Value > 0 ? max / _reserve.Value : 10f;
        void Tune(string mode, ArchetypeDataBlock a)
        {
            a.Damage = 0f; // the grenade does the damage
            a.PrecisionDamageMulti = 0f;
            a.DefaultClipSize = Math.Max(1, _clip.Value);
            a.CostOfBullet = cost;
            a.ShotDelay = Math.Max(0.1f, _delay.Value);
            a.FireMode = eWeaponFireMode.Semi;
        }
        GearFactory.OwnArchetypes(_cat, Key, _type.Value, Tune);
        // A machine gun has no semi archetype: make one from its auto.
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(_cat);
        if (cat != null && cat.SemiArchetype == 0)
        {
            uint src = cat.AutoArchetype != 0 ? cat.AutoArchetype : cat.BurstArchetype;
            if (src != 0)
            {
                uint id = GearFactory.CloneArchetype(src, Key + "_Arch_SemiMade");
                var a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
                if (a != null) { Tune("Semi", a); a.PublicName = GearFactory.Text(_type.Value); cat.SemiArchetype = id; }
            }
        }

        var comps = new Dictionary<int, uint?>
        {
            [(int)eGearComponent.Category] = _cat,
            [(int)eGearComponent.FireMode] = (uint)eWeaponFireMode.Semi,
        };
        foreach (var kv in GearFactory.ParseParts(_parts.Value, "GrenadeLauncher")) comps[kv.Key] = kv.Value;
        GearFactory.AddGear(body, Key + "_Gun", _name.Value, comps);
        ModInfo.Log.LogInfo($"[GrenadeLauncher] {_name.Value}: {_clip.Value} + {_reserve.Value} grenades (round cost {cost:0.##} of {max:0}), {_delay.Value:0.##} s, grenade {Grenades.LauncherDamage} dmg / {Grenades.LauncherRadius:0.#} m");
    }

    private static int _fails;

    /// <summary>Each shot of the launcher (on the shooter's machine) lobs a grenade along the view; the item is synced to all.</summary>
    [HarmonyPatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private static class Patch_Fire
    {
        private static void Postfix(BulletWeapon __instance)
        {
            try
            {
                if (_cat == 0 || __instance == null) return;
                var g = __instance.GearIDRange;
                if (g == null || g.GetCompID(eGearComponent.Category) != _cat) return;
                var owner = __instance.Owner;
                if (!GameUtil.IsLocalHuman(owner)) return; // bots fire it harmlessly
                var cam = owner.FPSCamera;
                Vector3 dir = cam != null ? cam.transform.forward : owner.transform.forward;
                Vector3 from = (cam != null ? cam.Position : owner.EyePosition) + dir * 0.7f;
                var right = Vector3.Cross(Vector3.up, dir);
                if (right.sqrMagnitude < 1e-4f) right = owner.transform.right;
                dir = Quaternion.AngleAxis(-Math.Abs(_loft.Value), right.normalized) * dir;
                if (!Grenades.Throw(owner, from, dir.normalized * Math.Max(1f, _speed.Value), owner.CourseNode, launcher: true) && _fails++ < 3)
                    ModInfo.Log.LogWarning("[GrenadeLauncher] no grenade item — is the Grenades module on?");
            }
            catch (Exception e) { if (_fails++ < 3) ModInfo.Log.LogWarning($"[GrenadeLauncher] {e.Message}"); }
        }
    }
}
