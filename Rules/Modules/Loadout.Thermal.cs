using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GtfoPunk.Core;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Thermal vision perk (EXPERIMENT). Two weapons in the game have thermal sights (parts Sight_10_t, Sight_19_t);
/// the effect lives in the material of the sight lens. The perk takes that material and stretches it over the whole view:
/// a quad right in front of the camera, toggled with a key.
/// Also writes a dump of every thermal-looking material (shader, properties) to BepInEx/config/GtfoPunk.ThermalDump.txt.
/// </summary>
public partial class Loadout
{
    private static BepInEx.Configuration.ConfigEntry<string> _thermalKey, _thermalMaterial, _thermalSearch;
    private static BepInEx.Configuration.ConfigEntry<float> _thermalDistance;
    private static BepInEx.Configuration.ConfigEntry<bool> _thermalItemCamera;

    private void BindThermal()
    {
        _thermalKey = Bind("ThermalKey", "N", "Thermal perk: key that switches thermal vision on/off (Unity KeyCode name).");
        _thermalMaterial = Bind("ThermalMaterial", "", "Thermal perk: exact material name to use (see GtfoPunk.ThermalDump.txt). Empty — pick automatically.");
        _thermalSearch = Bind("ThermalSearch", "thermal,_t_,sight_10_t,sight_19_t,heat,infrared",
            "Thermal perk: words to look for in material / shader / object names when picking and dumping (comma-separated).");
        _thermalDistance = Bind("ThermalDistance", 0.12f, "Thermal perk: how far in front of the camera the thermal screen sits (m).");
        _thermalItemCamera = Bind("ThermalOnItemCamera", true, "Thermal perk: put the screen on the camera that draws the weapon (like the sight lens). false — on the world camera.");
    }

    private static GameObject _thermalQuad;
    private static Camera _thermalCam;
    private static Material _thermalMat;
    private static bool _thermalOn, _thermalDumped, _thermalSearched;

    private static float _dumpAt = -1f;

    private static void ThermalTick(bool inLevel, bool perk)
    {
        if (!inLevel) _dumpAt = -1f;
        // One-time diagnostic dump a few seconds into the first level (whatever the perk).
        if (inLevel && !_thermalDumped)
        {
            if (_dumpAt < 0f) _dumpAt = Time.time + 5f;
            else if (Time.time >= _dumpAt) { _thermalDumped = true; try { DumpThermal(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Thermal] dump: {e.Message}"); } }
        }
        if (!inLevel || !perk)
        {
            if (_thermalOn) SetThermal(false);
            return;
        }

        if (Enum.TryParse<KeyCode>(_thermalKey.Value, true, out var key) && Input.GetKeyDown(key))
            SetThermal(!_thermalOn);

        if (_thermalOn) Fit();
    }

    private static void SetThermal(bool on)
    {
        if (!on)
        {
            if (_thermalQuad != null) UnityEngine.Object.Destroy(_thermalQuad);
            _thermalQuad = null; _thermalCam = null; _thermalOn = false;
            ModInfo.Log.LogInfo("[Thermal] off");
            return;
        }
        var agent = GameUtil.LocalAgent;
        var fps = agent?.FPSCamera;
        if (fps == null) { ModInfo.Log.LogWarning("[Thermal] no player camera"); return; }
        var mat = ThermalMaterial();
        if (mat == null) { ModInfo.Log.LogWarning("[Thermal] no thermal material found — see GtfoPunk.ThermalDump.txt and set ThermalMaterial"); return; }

        var cam = _thermalItemCamera.Value && fps.m_itemCamera != null ? fps.m_itemCamera : fps.GetComponent<Camera>();
        if (cam == null) { ModInfo.Log.LogWarning("[Thermal] camera not found"); return; }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "GtfoPunk_Thermal";
        var col = quad.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        quad.transform.SetParent(cam.transform, false);
        // Put it on a layer this camera draws (prefer the sight lens' own layer).
        int layer = _lensLayer >= 0 && (cam.cullingMask & (1 << _lensLayer)) != 0 ? _lensLayer : FirstLayerIn(cam.cullingMask);
        quad.layer = layer;
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        _thermalQuad = quad; _thermalCam = cam; _thermalOn = true;
        Fit();
        ModInfo.Log.LogInfo($"[Thermal] on: material '{mat.name}' (shader '{mat.shader?.name}'), camera '{cam.name}' (fov {cam.fieldOfView:0}, near {cam.nearClipPlane:0.###}), layer {layer} '{LayerMask.LayerToName(layer)}'");
    }

    /// <summary>Scale the quad to cover the camera's whole view (FOV changes when aiming).</summary>
    private static void Fit()
    {
        if (_thermalQuad == null || _thermalCam == null) { _thermalOn = false; return; }
        float d = Math.Max(_thermalCam.nearClipPlane + 0.01f, _thermalDistance.Value);
        float h = 2f * d * Mathf.Tan(_thermalCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        float w = h * Math.Max(0.1f, _thermalCam.aspect);
        _thermalQuad.transform.localPosition = new Vector3(0f, 0f, d);
        _thermalQuad.transform.localRotation = Quaternion.identity;
        _thermalQuad.transform.localScale = new Vector3(w, h, 1f);
    }

    private static int FirstLayerIn(int mask)
    {
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    private static int _lensLayer = -1;

    private static string[] SearchWords() =>
        _thermalSearch.Value.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();

    private static bool Matches(string s, string[] words)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.ToLowerInvariant();
        return words.Any(w => s.Contains(w));
    }

    /// <summary>The configured material, or the best automatic guess: a renderer material on a thermal sight part,
    /// then any material whose shader / name mentions "thermal".</summary>
    private static Material ThermalMaterial()
    {
        if (_thermalMat != null) return _thermalMat;
        if (_thermalSearched) return null;
        _thermalSearched = true;
        string want = _thermalMaterial.Value.Trim();
        Material pick = null;

        // 1) Lens of a thermal sight part.
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>()))
        {
            var g = o?.TryCast<GameObject>();
            if (g == null || g.name == null) continue;
            string n = g.name.ToLowerInvariant();
            if (!n.StartsWith("sight_") || !n.Contains("_t")) continue;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    bool ok = want.Length > 0 ? m.name == want : Matches(m.name, new[] { "thermal" }) || Matches(m.shader?.name, new[] { "thermal" });
                    if (ok && pick == null) { pick = m; _lensLayer = r.gameObject.layer; }
                }
        }
        // 2) Any loaded material.
        if (pick == null)
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                var m = o?.TryCast<Material>();
                if (m == null) continue;
                bool ok = want.Length > 0 ? m.name == want : Matches(m.shader?.name, new[] { "thermal" }) || Matches(m.name, new[] { "thermal" });
                if (ok) { pick = m; break; }
            }
        if (pick != null)
        {
            _thermalMat = new Material(pick) { name = pick.name + " (GtfoPunk thermal)" };
            ModInfo.Log.LogInfo($"[Thermal] using material '{pick.name}', shader '{pick.shader?.name}', lens layer {_lensLayer}");
        }
        return _thermalMat;
    }

    /// <summary>Writes what we know about thermal-looking materials and the thermal sight parts.</summary>
    private static void DumpThermal()
    {
        var words = SearchWords();
        var sb = new StringBuilder();
        sb.AppendLine($"GtfoPunk thermal dump, {DateTime.Now}");
        sb.AppendLine("== GameObjects (sight parts and anything matching) ==");
        int objs = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>()))
        {
            var g = o?.TryCast<GameObject>();
            if (g == null || g.transform.parent != null) continue; // roots only
            if (!Matches(g.name, words)) continue;
            if (objs++ > 40) break;
            sb.AppendLine($"[{g.name}] prefab={!g.scene.IsValid()} layer={g.layer}");
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                sb.Append($"   renderer '{r.name}' ({r.GetIl2CppType().Name}) layer {r.gameObject.layer}:");
                foreach (var m in r.sharedMaterials) sb.Append($" '{m?.name}'<{m?.shader?.name}>");
                sb.AppendLine();
            }
        }
        sb.AppendLine("== Materials matching ==");
        var seenShaders = new HashSet<string>();
        int mats = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
        {
            var m = o?.TryCast<Material>();
            if (m == null) continue;
            string sh = m.shader?.name;
            if (!Matches(m.name, words) && !Matches(sh, words)) continue;
            if (mats++ > 60) break;
            sb.AppendLine($"material '{m.name}' shader '{sh}' queue {m.renderQueue} keywords [{string.Join(" ", m.shaderKeywords ?? new string[0])}]");
            if (sh != null && seenShaders.Add(sh)) DumpShader(sb, m);
        }
        sb.AppendLine("== All shader names (for reference) ==");
        var names = new SortedSet<string>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>()))
        {
            var s = o?.TryCast<Shader>();
            if (s != null && s.name != null) names.Add(s.name);
        }
        foreach (var n in names) sb.AppendLine("  " + n);
        string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.ThermalDump.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
        ModInfo.Log.LogInfo($"[Thermal] dump written: {objs} objects, {mats} materials, {names.Count} shaders → {path}");
    }

    private static void DumpShader(StringBuilder sb, Material m)
    {
        try
        {
            var s = m.shader;
            int n = s.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = Shader.GetPropertyName(s, i);
                var type = Shader.GetPropertyType(s, i);
                string val = "";
                try
                {
                    val = type switch
                    {
                        UnityEngine.Rendering.ShaderPropertyType.Color => m.GetColor(name).ToString(),
                        UnityEngine.Rendering.ShaderPropertyType.Vector => m.GetVector(name).ToString(),
                        UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range => m.GetFloat(name).ToString("0.###"),
                        UnityEngine.Rendering.ShaderPropertyType.Texture => m.GetTexture(name)?.name ?? "none",
                        _ => "",
                    };
                }
                catch { }
                sb.AppendLine($"     {name} : {type} = {val}");
            }
        }
        catch (Exception e) { sb.AppendLine($"     (properties unavailable: {e.Message})"); }
    }
}
