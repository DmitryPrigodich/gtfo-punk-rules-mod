using Il2CppInterop.Runtime;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// The camera that draws a UI layer (lobby, HUD). GTFO's UI cameras are often disabled and rendered by hand, so
/// Camera.allCameras doesn't list them: fall back to a one-time search of all cameras. Cached per layer.
/// </summary>
public static class UiCamera
{
    private static readonly Dictionary<int, Camera> _cache = new();
    private static readonly Dictionary<int, float> _nextSearch = new();

    public static Camera For(int layer)
    {
        if (_cache.TryGetValue(layer, out var c) && c != null) return c;
        foreach (var cam in Camera.allCameras)
            if (cam != null && (cam.cullingMask & (1 << layer)) != 0) return _cache[layer] = cam;
        if (_nextSearch.TryGetValue(layer, out var at) && Time.time < at) return null;
        _nextSearch[layer] = Time.time + 5f;
        Camera best = null;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Camera>()))
        {
            var cam = o?.TryCast<Camera>();
            if (cam == null || (cam.cullingMask & (1 << layer)) == 0 || !cam.gameObject.scene.IsValid()) continue;
            if (best == null || (cam.orthographic && !best.orthographic)) best = cam;
        }
        if (best != null) ModInfo.Log.LogInfo($"[UiCamera] layer {layer} ({LayerMask.LayerToName(layer)}): camera '{best.name}' (enabled {best.enabled}, ortho {best.orthographic})");
        else ModInfo.Log.LogWarning($"[UiCamera] no camera draws layer {layer} ({LayerMask.LayerToName(layer)})");
        return best == null ? null : _cache[layer] = best;
    }

    /// <summary>Screen rect (pixels, y up) of a renderer's bounds as seen by the camera.</summary>
    public static Rect ScreenRect(Camera cam, Bounds b)
    {
        var a = cam.WorldToScreenPoint(b.min);
        var c = cam.WorldToScreenPoint(b.max);
        return Rect.MinMaxRect(Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y));
    }
}
