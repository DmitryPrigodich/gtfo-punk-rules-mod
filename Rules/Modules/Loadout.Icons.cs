using GameData;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Lobby slot pictures: drawn icons (white on transparent, 256×256) shipped with the mod — packs, items and gadgets.
/// r2modman puts the package's files flat next to the DLL; a manual install may keep the icons folder.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<float> _iconSize;

    private void BindIcons()
    {
        _iconSize = BindMigrated(Section, "IconSize", 24f, "Size of the icons in the lobby slots, relative to the slot's \"+\" mark.",
            (Section, "SlotIconSize", null), (Section, "LobbyIconSize", "12"));
    }

    /// <summary>Picture keys of the gadgets (no item IDs of their own).</summary>
    private const uint BioIconId = 0xF0000001, ThermalIconId = 0xF0000002, HackerIconId = 0xF0000003, BootsIconId = 0xF0000004;

    private static uint GadgetIconId(PerkKind k) => k switch { PerkKind.BioTracker => BioIconId, PerkKind.Thermal => ThermalIconId, PerkKind.Hacker => HackerIconId, PerkKind.SoftBoots => BootsIconId, _ => 0 };

    // ---------- drawn icons (icons/*.png next to the mod's DLL) ----------

    /// <summary>File name (without .png) of each picture key.</summary>
    private static readonly Dictionary<uint, string> IconFiles = new()
    {
        [101] = "ammopack", [102] = "medipack", [127] = "toolpak", [132] = "disinfection_pack",
        [Grenades.FragItemId] = "frag_grenade", [136] = "frag_grenade", [115] = "cfoam_grenade", [139] = "explosive_trip_mine", [144] = "cfoam_trip_mine",
        [117] = "fog_repeller", [114] = "glow_sticks", [116] = "lock_melter", [140] = "i2_lp_syringe",
        [BioIconId] = "bio_tracker", [ThermalIconId] = "thermal_goggles", [HackerIconId] = "hacking_tool", [BootsIconId] = "soft_boots",
    };

    private static readonly Dictionary<uint, Sprite> _iconSprites = new();
    private static string _iconDir;
    private static int _iconMissLogs;

    /// <summary>The drawn icon for the key, sized relative to the slot's "+" mark; null if there's no file.</summary>
    private static Sprite IconFile(uint id, Sprite reference)
    {
        if (_iconSprites.TryGetValue(id, out var s)) return s;
        _iconSprites[id] = null;
        _iconDir ??= System.IO.Path.GetDirectoryName(typeof(Loadout).Assembly.Location) ?? "";
        string name = IconFiles.TryGetValue(id, out var n) ? n : id.ToString();
        string path = new[]
        {
            System.IO.Path.Combine(_iconDir, "icons", name + ".png"), System.IO.Path.Combine(_iconDir, name + ".png"),
            System.IO.Path.Combine(_iconDir, "icons", id + ".png"), System.IO.Path.Combine(_iconDir, id + ".png"),
        }.FirstOrDefault(System.IO.File.Exists);
        if (path == null) { if (_iconMissLogs++ < 3) ModInfo.Log.LogInfo($"[Loadout] no icon file {name}.png in {_iconDir}"); return null; }
        var tex = Png.Load(path);
        if (tex == null) return null;
        float refWidth = reference != null ? reference.bounds.size.x : 1f;
        float ppu = tex.width / Math.Max(0.0001f, refWidth * Math.Max(0.1f, _iconSize.Value));
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        ModInfo.Log.LogInfo($"[Loadout] icon {name}.png loaded ({tex.width}x{tex.height})");
        return _iconSprites[id] = s;
    }
}
