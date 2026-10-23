using System;
using System.Collections.Generic;
using System.Linq;
using GameData;
using Gear;
using GtfoPunk.Core;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// "Photos" of items for the lobby loadout slots, rendered by the game's own gear icon renderer (same light and look
/// as the weapon icons). We register an icon job for a dummy gear marked with the item ID and, when the renderer loads
/// "the gear", hand it a copy of the item's pickup model instead.
/// </summary>
public partial class Loadout
{
    private static BepInEx.Configuration.ConfigEntry<bool> _photos;
    private static BepInEx.Configuration.ConfigEntry<float> _photoSize;

    private void BindIcons()
    {
        _photos = Bind("ItemPhotos", true, "Lobby slots show rendered pictures of the items (like weapon icons) instead of the small inventory symbols.");
        _photoSize = Bind("ItemPhotoSize", 2.5f, "Size of the item pictures in the lobby slots, relative to the slot's \"+\" symbol.");
    }

    private const string PhotoMarker = "GtfoPunkIcon_";
    private static readonly Dictionary<uint, Texture2D> _photoTex = new();
    private static readonly Dictionary<uint, Sprite> _photoSprites = new();
    private static readonly HashSet<uint> _photoRequested = new();
    private static float _photoPpu = -1f;

    /// <summary>In the lobby, once a second: queue pictures not made yet (a couple at a time).</summary>
    private static void PhotoTick()
    {
        if (!_photos.Value || GearIconRendering.Current == null) return;
        int started = 0;
        foreach (var id in PackItemIds.Where(x => x != 0).Concat(ItemChoices()))
        {
            if (_photoRequested.Contains(id)) continue;
            _photoRequested.Add(id);
            try { RequestPhoto(id); started++; }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] photo of item {id}: {e.Message}"); }
            if (started >= 2) break;
        }
    }

    private static void RequestPhoto(uint id)
    {
        var baseGear = GameDataBlockBase<PlayerOfflineGearDataBlock>.GetAllBlocks().ToArray().FirstOrDefault(b => b != null && !string.IsNullOrEmpty(b.GearJSON));
        if (baseGear == null) return;
        var range = new GearIDRange(baseGear.GearJSON);
        range._PublicGearName_k__BackingField = PhotoMarker + id;
        range._m_checksum_k__BackingField = 0x47500000u + id; // unique, so the renderer doesn't reuse a cached weapon icon
        var settings = new IconRenderSettings { resX = 256, resY = 256, zoom = 1f, rotation = 0f, offset = Vector2.zero, type = eGearIconType.LoadoutBar };
        var cb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<IconRenderJobResult>>(new Action<IconRenderJobResult>(r => OnPhoto(id, r)));
        GearIconRendering.RegisterIconJob(range, cb, settings);
        ModInfo.Log.LogInfo($"[Loadout] photo of item {id} requested");
    }

    [HarmonyPatch(typeof(GearIconRendering), nameof(GearIconRendering.LoadGearForIconJob))]
    private static class Patch_IconLoad
    {
        private static bool Prefix(GearIconRendering.IconRenderJob job)
        {
            try
            {
                string name = job?.gearToRender?.PublicGearName;
                if (name == null || !name.StartsWith(PhotoMarker)) return true;
                uint id = uint.Parse(name.Substring(PhotoMarker.Length));
                GameObject prefab = null;
                var list = ItemSpawnManager.GetItemPrefabs(id, ItemMode.Pickup);
                if (list != null)
                    foreach (var p in list)
                        if (p != null && p.GetComponentsInChildren<Renderer>(true).Length > 0) { prefab = p; break; }
                var go = prefab != null ? TurretVariants.CopyModel(prefab, null, "GtfoPunk_IconModel_" + id) : new GameObject("GtfoPunk_IconModel_empty");
                go.transform.position = new Vector3(0f, -2000f, 0f);
                job.loadedGearGO = go;
                job.asyncLoadingStarted = true;
                job.asyncLoadingDone = true;
                ModInfo.Log.LogInfo($"[Loadout] photo of item {id}: model {(prefab != null ? prefab.name : "none")} handed to the icon renderer");
                return false;
            }
            catch (Exception e)
            {
                ModInfo.Log.LogWarning($"[Loadout] photo model: {e.Message}");
                return true;
            }
        }
    }

    private static void OnPhoto(uint id, IconRenderJobResult r)
    {
        try
        {
            var rt = r?.icon;
            if (rt == null) { ModInfo.Log.LogWarning($"[Loadout] photo of item {id}: no picture"); return; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _photoTex[id] = tex;
            _photoSprites.Remove(id);
            ModInfo.Log.LogInfo($"[Loadout] photo of item {id} ready ({rt.width}x{rt.height})");
            RepaintAll();
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Loadout] photo of item {id}: {e.Message}"); }
    }

    /// <summary>The item's picture sized relative to the slot's original "+" symbol, or null if not rendered (yet).</summary>
    private static Sprite Photo(uint id, Sprite reference)
    {
        if (!_photos.Value || !_photoTex.TryGetValue(id, out var tex) || tex == null) return null;
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
