using GameData;
using Gear;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "DRED KHLOPUSHKA GL-10": a special-slot grenade launcher. Each shot lobs one small contact grenade — the TGL3 sentry's
/// (module Grenades: proximity fuse, mine-like blast) — a bit weaker and smaller: 35 damage at the centre, 3.5 m.
/// A 10-grenade box magazine, 30 in the reserve, semi-auto (0.6 s). The gun's own bullets do nothing.
/// Look: on a TechMan Klust 6 (its iron-sight aim; 0.9.36's Omneco LRG aimed through a scope) — Buckland AF6's front,
/// Omneco's machine-gun receiver, Buckland XDist2's stock and iron sights, Klust 6's drum, flashlight E. A tiny grenade,
/// 1.2 s between shots, a slug shotgun's kick and a shake. The blast hurts players too — don't fire point blank.
/// Config: [Weapons.Khlopushka] — the same gun keys as everywhere (Name, Type, Body, Parts, Magazine, Reserve, ShotDelay,
/// RecoilFrom) and the launcher's own (Grenade…, Loft, Shake). Everyone needs the mod (new gear).
/// </summary>
public class GrenadeLauncher : ModuleBase
{
    public override string Id => "GrenadeLauncher";
    public override string Description => "DRED KHLOPUSHKA GL-10: special-slot grenade launcher — lobs small contact grenades (35 damage, 3.5 m), 10 + 30. Everyone needs it.";
    public override string Section => "Weapons.Khlopushka";
    public override IReadOnlyList<string> OldSections => new[] { "GrenadeLauncher" };
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<string> _name, _body, _parts, _type, _recoil;
    private static ConfigEntry<int> _clip, _reserve;
    private static ConfigEntry<float> _delay, _damage, _radius, _speed, _loft, _size, _shake;

    protected override void OnBindConfig()
    {
        _name = Bind("Name", "DRED KHLOPUSHKA GL-10", "Name in the weapon list.");
        _type = BindMigrated(Section, "Type", "Grenade Launcher", "Weapon type shown above the name.", (Section, "WeaponType", "Grenade Launcher"));
        _body = BindMigrated(Section, "Body", "TechMan Klust 6", "The gun whose parts, hand animations, aim and attachments are the base (special slot).", (Section, "Body", "Omneco LRG"));
        _parts = Bind("Parts", "12=12, 16=10, 19=12, 21=43, 23=16, 25=5",
            "Parts on the body: 'type=ID, …' — 12 front, 16 receiver, 19 stock, 21 sight, 23 magazine, 25 flashlight; 'type=<gun name>', 'type=-' — none.");
        _recoil = Bind("RecoilFrom", "Bataldo Custom K330", "The recoil of this gun. Empty — the body's.");
        _clip = Bind("Magazine", 10, "Grenades in a magazine.");
        _reserve = Bind("Reserve", 30, "Grenades the special-ammo reserve holds when full.");
        _delay = BindMigrated(Section, "ShotDelay", 1.2f, "Seconds between shots (semi-auto).", (Section, "ShotDelay", "0.6"));
        _damage = BindMigrated(Section, "GrenadeDamage", 35f, "Grenade damage at the blast centre (fades with distance). The TGL3 sentry's: 40.", (Section, "Damage", "35"));
        _radius = BindMigrated(Section, "GrenadeRadius", 3.5f, "Blast radius (m). The TGL3 sentry's: 5.", (Section, "Radius", "3.5"));
        _speed = BindMigrated(Section, "GrenadeSpeed", 20f, "Launch speed (m/s) — lower lobs higher.", (Section, "Speed", "20"));
        _size = Bind("GrenadeSize", 0.45f, "Size of the flying grenade against the sentry's (1).");
        _loft = Bind("Loft", 4f, "Degrees the grenade flies above the aim line (to lob it a little).");
        _shake = Bind("Shake", 1.2f, "Camera shake when firing (0 — none).");
    }

    protected override void OnEnable() => GearFactory.OnInject += Inject;

    private static uint _cat;
    private const string Key = "GtfoPunk_GrenadeLauncher";

    private static void Inject()
    {
        Grenades.LauncherRadius = Math.Max(0.5f, _radius.Value);
        Grenades.LauncherDamage = (int)Math.Max(1f, _damage.Value);
        Grenades.LauncherScale = Mathf.Clamp(_size.Value, 0.1f, 2f);
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
        uint recoil = 0;
        var rg = string.IsNullOrWhiteSpace(_recoil.Value) ? null : GearFactory.FindByName(_recoil.Value);
        if (rg != null && GearFactory.GetComps(rg.GearJSON).TryGetValue((int)eGearComponent.Category, out var rcat))
        {
            var c = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(rcat);
            uint ra = c == null ? 0 : c.SemiArchetype != 0 ? c.SemiArchetype : c.AutoArchetype != 0 ? c.AutoArchetype : c.BurstArchetype;
            recoil = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(ra)?.RecoilDataID ?? 0;
        }
        void TuneAll(string mode, ArchetypeDataBlock a) { Tune(mode, a); if (recoil != 0) a.RecoilDataID = recoil; }
        GearFactory.OwnArchetypes(_cat, Key, _type.Value, TuneAll);
        // A machine gun has no semi archetype: make one from its auto.
        var cat = GameDataBlockBase<GearCategoryDataBlock>.GetBlock(_cat);
        if (cat != null && cat.SemiArchetype == 0)
        {
            uint src = cat.AutoArchetype != 0 ? cat.AutoArchetype : cat.BurstArchetype;
            if (src != 0)
            {
                uint id = GearFactory.CloneArchetype(src, Key + "_Arch_SemiMade");
                var a = GameDataBlockBase<ArchetypeDataBlock>.GetBlock(id);
                if (a != null) { TuneAll("Semi", a); a.PublicName = GearFactory.Text(_type.Value); cat.SemiArchetype = id; }
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
                if (_shake.Value > 0f) Shake(owner.Position, _shake.Value);
            }
            catch (Exception e) { if (_fails++ < 3) ModInfo.Log.LogWarning($"[GrenadeLauncher] {e.Message}"); }
        }
    }

    /// <summary>A short kick of the camera at the shooter.</summary>
    private static void Shake(Vector3 pos, float amount)
    {
        var go = new GameObject("GtfoPunk_LauncherShake");
        go.transform.position = pos;
        var s = go.AddComponent<CameraShakeEffect>();
        s.Radius = 3f;
        s.Duration = 0.35f;
        s.Amplitude = amount;
        s.Frequency = 30f;
        s.directional = false;
        s.Play();
        UnityEngine.Object.Destroy(go, 1f);
    }
}
