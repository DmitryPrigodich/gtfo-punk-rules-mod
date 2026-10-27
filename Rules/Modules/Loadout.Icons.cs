using GameData;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Lobby slot pictures: white silhouettes of the item models (like the game's weapon icons), rendered by our own
/// small camera — packs, items and gadgets alike, upright and filling the slot.
/// The model is turned to show its largest side, laid horizontally, tilted a little for some depth.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<bool> _photos;
    private static ConfigEntry<float> _photoSize, _photoTilt, _iconSize;

    private void BindIcons()
    {
        _photos = Bind("ItemPictures", false, "Lobby slots show silhouettes of the pack / item / gadget models. false — text only.");
        _photoSize = BindMigrated(Id, "ItemPictureSize", 3f, "Size of the item pictures in the lobby slots, relative to the slot's \"+\" symbol.", (Id, "ItemPhotoSize", "2.5"));
        _photoTilt = Bind("ItemPictureTilt", 12f, "Tilt of the item models in the pictures (degrees; 0 — flat side view).");
        _iconSize = BindMigrated(Id, "LobbyIconSize", 12f, "Size of the drawn icons in the lobby slots, relative to the slot's \"+\" symbol (they're shown upright).", (Id, "ItemIconSize", "6"));
        _iconFiles = Bind("ItemIcons", true, "Lobby slots show the drawn icons from the mod's icons folder (icons/<name>.png, 256×256, white on transparent). They come before the model pictures.");
    }

    /// <summary>Picture keys of the gadgets (no item IDs of their own).</summary>
    private const uint BioIconId = 0xF0000001, ThermalIconId = 0xF0000002, HackerIconId = 0xF0000003, BootsIconId = 0xF0000004;
    private const int PhotoRes = 256, PhotoSuper = 2;

    private static readonly Dictionary<uint, Texture2D> _photoTex = new();
    private static readonly Dictionary<uint, Sprite> _photoSprites = new();
    private static readonly Dictionary<uint, int> _photoTries = new();
    private static float _photoPpu = -1f;

    private static uint GadgetIconId(PerkKind k) => k switch { PerkKind.BioTracker => BioIconId, PerkKind.Thermal => ThermalIconId, PerkKind.Hacker => HackerIconId, PerkKind.SoftBoots => BootsIconId, _ => 0 };

    // ---------- drawn icons (icons/*.png next to the mod's DLL) ----------

    private static ConfigEntry<bool> _iconFiles;

    /// <summary>File name (without .png) of each picture key.</summary>
    private static readonly Dictionary<uint, string> IconFiles = new()
    {
        [101] = "ammopack", [102] = "medipack", [127] = "toolpak", [132] = "disinfection_pack",
        [136] = "frag_grenade", [115] = "cfoam_grenade", [139] = "explosive_trip_mine", [144] = "cfoam_trip_mine",
        [117] = "fog_repeller", [114] = "glow_sticks", [116] = "lock_melter", [140] = "i2_lp_syringe",
        [BioIconId] = "bio_tracker", [ThermalIconId] = "thermal_goggles", [HackerIconId] = "hacking_tool", [BootsIconId] = "soft_boots",
    };

    private static readonly Dictionary<uint, Sprite> _iconSprites = new();
    private static string _iconDir;
    private static int _iconMissLogs;

    /// <summary>The drawn icon for the key, sized like the item pictures; null if there's no file.</summary>
    private static Sprite IconFile(uint id, Sprite reference)
    {
        if (!_iconFiles.Value) return null;
        if (_iconSprites.TryGetValue(id, out var s)) return s;
        _iconSprites[id] = null;
        // r2modman puts the package's files flat next to the DLL; a manual install may keep the icons folder.
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

    /// <summary>In the lobby, once a second: render one picture not made yet (a model that isn't loaded is retried a few times).</summary>
    private static void PhotoTick()
    {
        if (!_photos.Value) return;
        var ids = PackItemIds.Where(x => x != 0).Concat(ItemChoices()).Append(BioIconId).Append(ThermalIconId).Where(x => !IconFiles.ContainsKey(x) || !_iconFiles.Value);
        foreach (var id in ids)
        {
            if (_photoTex.ContainsKey(id)) continue;
            _photoTries.TryGetValue(id, out int tries);
            if (tries >= 3) continue;
            _photoTries[id] = tries + 1;
            var model = PhotoModel(id);
            if (model == null) { if (tries == 2) ModInfo.Log.LogInfo($"[Loadout] picture of {PhotoName(id)}: model not loaded — text only"); continue; }
            try
            {
                var tex = RenderSilhouette(model);
                if (tex == null) continue;
                _photoTex[id] = tex;
                _photoSprites.Remove(id);
                ModInfo.Log.LogInfo($"[Loadout] picture of {PhotoName(id)} ready (model '{model.name}')");
                RepaintAll();
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] picture of {PhotoName(id)}: {e.Message}"); }
            return; // one per tick
        }
    }

    private static string PhotoName(uint id) => id == BioIconId ? "bio-tracker" : id == ThermalIconId ? "thermal goggles" : $"item {id}";

    private static GameObject PhotoModel(uint id)
    {
        if (id == BioIconId) return TurretVariants.KeptModel("33=3");
        if (id == ThermalIconId) return TurretVariants.KeptModel("Sight_19_t,Sight_10_t");
        if (id == Grenades.GrenadeItemId) id = 115; // the grenade wears the C-Foam Grenade's look
        var list = ItemSpawnManager.GetItemPrefabs(id, ItemMode.Pickup);
        if (list == null) return null;
        foreach (var p in list)
            if (p != null && p.GetComponentsInChildren<Renderer>(true).Length > 0) return p;
        return null;
    }

    private static int _photoLayer = -1;

    /// <summary>An unnamed layer nothing else draws on.</summary>
    private static int PhotoLayer()
    {
        if (_photoLayer >= 0) return _photoLayer;
        for (int i = 31; i > 8; i--)
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return _photoLayer = i;
        return _photoLayer = 31;
    }

    /// <summary>White silhouette of the model on a transparent background (rendered at 2× and averaged — smooth edges).</summary>
    private static Texture2D RenderSilhouette(GameObject prefab)
    {
        int layer = PhotoLayer();
        var root = TurretVariants.CopyModel(prefab, null, "GtfoPunk_PhotoModel", quiet: true);
        var rt = RenderTexture.GetTemporary(PhotoRes * PhotoSuper, PhotoRes * PhotoSuper, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("GtfoPunk_PhotoCamera");
        try
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
            var white = PhotoMaterial();
            if (white != null)
                foreach (var r in renderers)
                {
                    var mats = new Material[Math.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = white;
                    r.sharedMaterials = mats;
                }

            // Show the largest side: view along the thinnest axis, the longest one horizontal.
            var t = root.transform;
            t.position = new Vector3(0f, -3000f, 0f);
            t.rotation = Quaternion.identity;
            var b = Bounds(renderers);
            var axes = new[] { (Vector3.right, b.size.x), (Vector3.up, b.size.y), (Vector3.forward, b.size.z) }.OrderBy(a => a.Item2).ToArray();
            var look = Quaternion.LookRotation(axes[0].Item1, axes[1].Item1); // thin axis → camera forward, middle axis → up
            t.rotation = Quaternion.Euler(-_photoTilt.Value * 0.5f, _photoTilt.Value, 0f) * Quaternion.Inverse(look);
            b = Bounds(renderers);

            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = Math.Max(b.extents.x, b.extents.y) * 1.05f;
            cam.aspect = 1f;
            cam.transform.position = b.center - Vector3.forward * (b.extents.z + 5f);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = b.size.z + 10f;
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            big.Apply();
            RenderTexture.active = prev;

            var src = big.GetPixels32();
            UnityEngine.Object.Destroy(big);
            var dst = new Color32[PhotoRes * PhotoRes];
            int w = PhotoRes * PhotoSuper, filled = 0;
            for (int y = 0; y < PhotoRes; y++)
                for (int x = 0; x < PhotoRes; x++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < PhotoSuper; dy++)
                        for (int dx = 0; dx < PhotoSuper; dx++)
                        {
                            var c = src[(y * PhotoSuper + dy) * w + x * PhotoSuper + dx];
                            if (c.a > 3 || c.r > 3 || c.g > 3 || c.b > 3) sum++;
                        }
                    byte a = (byte)(255 * sum / (PhotoSuper * PhotoSuper));
                    if (a > 0) filled++;
                    dst[y * PhotoRes + x] = new Color32(255, 255, 255, a);
                }
            if (filled < 20) { ModInfo.Log.LogWarning($"[Loadout] picture of '{prefab.name}' came out empty ({(white == null ? "no unlit shader" : "nothing drawn")})"); return null; }
            var tex = new Texture2D(PhotoRes, PhotoRes, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(dst);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
        finally
        {
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(root);
        }
    }

    private static Material _photoMat;
    private static bool _photoMatTried;

    /// <summary>A plain white unlit material (any of the built-in unlit shaders that made it into the build).</summary>
    private static Material PhotoMaterial()
    {
        if (_photoMatTried) return _photoMat;
        _photoMatTried = true;
        foreach (var name in new[] { "Unlit/Color", "Hidden/Internal-Colored", "Sprites/Default", "UI/Default" })
        {
            var sh = Shader.Find(name);
            if (sh == null) continue;
            _photoMat = new Material(sh) { color = Color.white, hideFlags = HideFlags.DontUnloadUnusedAsset };
            ModInfo.Log.LogInfo($"[Loadout] item pictures: shader {name}");
            return _photoMat;
        }
        ModInfo.Log.LogWarning("[Loadout] item pictures: no unlit shader — drawing with the models' own materials");
        return null;
    }

    private static Bounds Bounds(Renderer[] renderers)
    {
        Bounds b = default;
        bool any = false;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>The picture sized relative to the slot's original "+" symbol, or null if not rendered (yet).</summary>
    private static Sprite Photo(uint id, Sprite reference)
    {
        if (!_photos.Value || id == 0 || !_photoTex.TryGetValue(id, out var tex) || tex == null) return null;
        if (_photoSprites.TryGetValue(id, out var s) && s != null) return s;
        if (_photoPpu < 0f)
        {
            float refWidth = reference != null ? reference.bounds.size.x : 1f;
            _photoPpu = tex.width / Math.Max(0.0001f, refWidth * Math.Max(0.1f, _photoSize.Value));
        }
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), _photoPpu);
        s.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _photoSprites[id] = s;
        return s;
    }
}
