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
    private static BepInEx.Configuration.ConfigEntry<string> _thermalZoomKey, _thermalZoomLevels, _thermalMaterial, _thermalSearch;
    private static BepInEx.Configuration.ConfigEntry<float> _thermalDistance;
    private static BepInEx.Configuration.ConfigEntry<bool> _thermalItemCamera, _thermalDump, _thermalReticle;
    private static BepInEx.Configuration.ConfigEntry<float> _thermalZoom, _thermalNoise;

    private void BindThermal()
    {
        _thermalZoomKey = Bind("ThermalZoomKey", "Z", "Thermal goggles: key that steps through the zoom levels (Unity KeyCode name).");
        _thermalZoomLevels = Bind("ThermalZoomLevels", "0,0.5,0.75", "Thermal goggles: zoom levels the zoom key steps through (0 — no zoom, 0.75 — like the thermal sight).");
        _thermalMaterial = Bind("ThermalMaterial", "", "Thermal goggles: exact material name to use (see GtfoPunk.ThermalDump.txt). Empty — pick automatically.");
        _thermalSearch = Bind("ThermalSearch", "thermal,_t_,sight_10_t,sight_19_t,heat,infrared",
            "Thermal goggles: words to look for in material / shader / object names when picking and dumping (comma-separated).");
        _thermalDistance = Bind("ThermalDistance", 0.12f, "Thermal goggles: how far in front of the camera the thermal screen sits (m).");
        _thermalZoom = Bind("ThermalZoom", 0f, "Thermal goggles: zoom when switched on (the sight uses 0.75 = strongly zoomed). 0 — like your eyes.");
        _thermalNoise = Bind("ThermalNoise", 1f, "Thermal goggles: amount of scanline / signal distortion (1 — like the sight, 2–3 — predator-style noise, 0 — clean). Applied live.");
        _thermalReticle = Bind("ThermalReticle", false, "Thermal goggles: keep the sight's reticle drawn over the whole view.");
        _thermalDump = Bind("ThermalDiagnostics", false, "Write GtfoPunk.ThermalDump.txt (thermal materials and shader settings) in the first level. Causes a hitch.");
        _thermalItemCamera = Bind("ThermalOnItemCamera", true, "Thermal goggles: put the screen on the camera that draws the weapon (like the sight lens). false — on the world camera.");
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
        if (inLevel && !_thermalDumped && _thermalDump.Value)
        {
            if (_dumpAt < 0f) _dumpAt = Time.time + 5f;
            else if (Time.time >= _dumpAt) { _thermalDumped = true; try { DumpThermal(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Thermal] dump: {e.Message}"); } }
        }
        if (!inLevel || !perk)
        {
            if (_thermalOn) SetThermal(false);
            return;
        }

        if (ChatOpen()) return;
        if (GadgetKeyDown()) { _zoom = Mathf.Clamp(_thermalZoom.Value, 0f, 0.99f); SetThermal(!_thermalOn); }
        if (_thermalOn && Enum.TryParse<KeyCode>(_thermalZoomKey.Value, true, out var zk) && Input.GetKeyDown(zk))
        {
            var levels = new List<float>();
            foreach (var part in _thermalZoomLevels.Value.Split(','))
                if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) levels.Add(Mathf.Clamp(z, 0f, 0.99f));
            if (levels.Count == 0) levels.Add(0f);
            int i = levels.FindIndex(z => Math.Abs(z - _zoom) < 0.01f);
            _zoom = levels[(i + 1) % levels.Count];
            _nextTune = 0f; // apply now
            try { CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, GameUtil.LocalAgent.Position); } catch { }
            ModInfo.Log.LogInfo($"[Thermal] zoom {_zoom:0.##}");
        }

        if (_thermalOn)
        {
            Fit();
            if (Time.time >= _nextTune) { _nextTune = Time.time + 0.5f; Tune(_thermalMat); }
        }
    }

    private static float _nextTune, _zoom;
    private static float _baseDistMin = -1f, _baseDistMax, _baseDistShadowMin, _baseDistShadowMax;

    /// <summary>Turn the sight lens material into a full-view filter: no magnification, no reticle, no round border.</summary>
    private static void Tune(Material m)
    {
        if (m == null) return;
        try
        {
            m.SetFloat("_Zoom", _zoom);
            if (!_thermalReticle.Value)
            {
                foreach (var n in new[] { "_ReticuleA", "_ReticuleB", "_ReticuleC" }) m.SetTexture(n, Texture2D.blackTexture);
                foreach (var n in new[] { "_ReticuleColorA", "_ReticuleColorB", "_ReticuleColorC" }) m.SetColor(n, Color.clear);
            }
            m.SetFloat("_ClipBorders", 0f);
            m.DisableKeyword("_CLIPBORDERS_ON");
            m.SetFloat("_SightDirt", 0f);
            if (_baseDistMin < 0f)
            {
                _baseDistMin = m.GetFloat("_DistortionMin"); _baseDistMax = m.GetFloat("_DistortionMax");
                _baseDistShadowMin = m.GetFloat("_DistortionMinShadowEnemies"); _baseDistShadowMax = m.GetFloat("_DistortionMaxShadowEnemies");
            }
            float k = Math.Max(0f, _thermalNoise.Value);
            m.SetFloat("_DistortionMin", Mathf.Clamp01(_baseDistMin * k));
            m.SetFloat("_DistortionMax", Mathf.Clamp01(_baseDistMax * k));
            m.SetFloat("_DistortionMinShadowEnemies", Mathf.Clamp01(_baseDistShadowMin * k));
            m.SetFloat("_DistortionMaxShadowEnemies", Mathf.Clamp01(_baseDistShadowMax * k));
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Thermal] tune: {e.Message}"); }
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
        Tune(mat);
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
