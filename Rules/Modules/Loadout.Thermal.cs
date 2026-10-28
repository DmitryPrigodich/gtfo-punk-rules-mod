using Il2CppInterop.Runtime;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Gadget "Thermal goggles". Two weapons in the game have thermal sights (parts Sight_10_t, Sight_19_t); the effect
/// lives in the material of the sight lens. The gadget takes that material and stretches it over the whole view: a quad
/// right in front of the weapon camera, toggled with the gadget key. Zoom levels, palettes (the game's own heat
/// gradients), noise and signal drop-outs on damage / a dying battery.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<string> _thermalZoomKey, _thermalZoomLevels;
    private static ConfigEntry<float> _thermalDistance;
    private static ConfigEntry<bool> _thermalReticle;
    private static ConfigEntry<float> _thermalZoom, _thermalNoise;
    private static ConfigEntry<string> _thermalPalette, _thermalPaletteKey;
    private static ConfigEntry<bool> _thermalGlitch;

    private void BindThermal()
    {
        _thermalZoom = BindMoved(SecThermal, "Zoom", 0f, "Zoom when switched on (0 — like your eyes, 0.75 — like the thermal sight).", "ThermalZoom");
        _thermalZoomLevels = BindMoved(SecThermal, "ZoomLevels", "0,0.5,0.75", "Zoom levels the zoom key steps through (comma-separated).", "ThermalZoomLevels");
        _thermalZoomKey = BindMoved(SecThermal, "ZoomKey", "Z", "Key that steps through the zoom levels (Unity KeyCode name; empty — off).", "ThermalZoomKey");
        _thermalPalette = BindMoved(SecThermal, "Palette", "", "Color palette: part of a palette texture's name (the log lists them: '[Thermal] palettes: …'). Empty — the sight's own. The palette key changes it and the choice is remembered here.", "ThermalPalette");
        _thermalPaletteKey = BindMoved(SecThermal, "PaletteKey", "X", "Key that steps through the palettes (Unity KeyCode name; empty — off).", "ThermalPaletteKey");
        _thermalNoise = BindMoved(SecThermal, "Noise", 1f, "Scanlines and signal distortion (1 — like the sight, 2–3 — heavy noise, 0 — clean picture).", "ThermalNoise");
        _thermalGlitch = BindMoved(SecThermal, "Glitch", true, "The picture breaks up when you take damage and gets noisier as the battery runs low (drop-outs below 15%).", "ThermalGlitch");
        _thermalReticle = BindMoved(SecThermal, "Reticle", false, "Keep the sight's reticle drawn over the whole view.", "ThermalReticle");
        _thermalDistance = BindMoved(SecThermal, "Distance", 0.12f, "How far in front of the camera the thermal screen sits (m). Raise it if the edges of the view stay uncovered.", "ThermalDistance");
    }

    private static GameObject _thermalQuad;
    private static Camera _thermalCam;
    private static Material _thermalMat;
    private static bool _thermalOn, _thermalSearched;

    private static void ThermalTick(bool inLevel, bool perk)
    {
        if (!inLevel || !perk)
        {
            if (_thermalQuad != null) SetThermal(false, instant: true);
            _prewarmAt = -1f; _prewarmed = false;
            return;
        }
        PrewarmThermal();
        if (_thermalQuad != null) Fit();
        if (_thermalPrewarming) return;

        if (ChatOpen()) return;
        if (GadgetKeyDown())
        {
            if (_thermalOn) SetThermal(false);
            else if (CanSwitchOn()) { _zoom = Mathf.Clamp(_thermalZoom.Value, 0f, 0.99f); SetThermal(true); }
        }
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

        if (_thermalOn && Time.time >= _nextTune) { _nextTune = Time.time + 0.5f; Tune(_thermalMat); }
        if (_thermalOn) { PaletteKey(); GlitchTick(); }
        else _lastHealth = -1f;
    }

    // ---------- palettes ----------

    private static readonly List<Texture> _palettes = new();
    private static bool _palettesFound;
    private static int _palette = -1; // -1 — the sight's own

    private static void FindPalettes()
    {
        if (_palettesFound) return;
        _palettesFound = true;
        try
        {
            var own = _thermalMat != null ? _thermalMat.GetTexture("_HeatTex") : null;
            if (own != null) _palettes.Add(own);
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Texture2D>()))
            {
                var t = o?.TryCast<Texture2D>();
                string n = t?.name?.ToLowerInvariant() ?? "";
                if (t == null || !n.Contains("gradient") || !(n.Contains("therm") || n.Contains("threm") || n.Contains("heat") || n.Contains("flir"))) continue;
                if (!_palettes.Any(p => p.name == t.name)) _palettes.Add(t);
            }
            ModInfo.Log.LogInfo($"[Thermal] palettes: {string.Join(", ", _palettes.Select(p => p.name))}");
            string want = _thermalPalette.Value.Trim().ToLowerInvariant();
            if (want.Length > 0) _palette = _palettes.FindIndex(p => p.name.ToLowerInvariant().Contains(want));
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Thermal] palettes: {e.Message}"); }
    }

    private static void PaletteKey()
    {
        FindPalettes();
        if (_palettes.Count > 1 && Enum.TryParse<KeyCode>(_thermalPaletteKey.Value, true, out var k) && Input.GetKeyDown(k))
        {
            _palette = (_palette + 1) % _palettes.Count;
            _thermalPalette.Value = _palettes[_palette].name; // remembered for the next time
            ModInfo.Log.LogInfo($"[Thermal] palette {_palettes[_palette].name}");
            try { CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, GameUtil.LocalAgent.Position); } catch { }
        }
        if (_palette >= 0 && _palette < _palettes.Count && _thermalMat != null) _thermalMat.SetTexture("_HeatTex", _palettes[_palette]);
    }

    // ---------- glitches: damage and a dying battery ----------

    private static float _lastHealth = -1f, _dropUntil, _noiseUntil, _nextRandomDrop;

    private static void GlitchTick()
    {
        if (!_thermalGlitch.Value || _thermalMat == null) return;
        var agent = GameUtil.LocalAgent;
        float hp = agent?.Damage != null ? agent.Damage.Health : -1f;
        if (_lastHealth >= 0f && hp >= 0f && hp < _lastHealth - 0.01f)
        {
            float hit = Mathf.Clamp01((_lastHealth - hp) / Math.Max(1f, agent.Damage.HealthMax) * 4f); // bigger hit — longer
            _dropUntil = Time.time + 0.06f + 0.12f * hit;
            _noiseUntil = Time.time + 0.4f + 0.6f * hit;
        }
        _lastHealth = hp;
        // A dying battery: below 15% the signal drops out now and then.
        if (_batterySeconds.Value > 0f && _charge < 0.15f && Time.time >= _nextRandomDrop)
        {
            _nextRandomDrop = Time.time + UnityEngine.Random.Range(1f, 4f) * Mathf.Lerp(0.3f, 1f, _charge / 0.15f);
            _dropUntil = Time.time + UnityEngine.Random.Range(0.05f, 0.15f);
            _noiseUntil = Math.Max(_noiseUntil, Time.time + 0.3f);
        }
        // Noise: more as the battery runs low (×1 above 30%, ×3 at 0), ×4 right after a hit.
        float k = Math.Max(0f, _thermalNoise.Value);
        if (_batterySeconds.Value > 0f && _charge < 0.3f) k *= Mathf.Lerp(3f, 1f, _charge / 0.3f);
        if (Time.time < _noiseUntil) k *= 4f;
        SetNoise(_thermalMat, k);
    }

    private static void SetNoise(Material m, float k)
    {
        if (_baseDistMin < 0f) return;
        m.SetFloat("_DistortionMin", Mathf.Clamp01(_baseDistMin * k));
        m.SetFloat("_DistortionMax", Mathf.Clamp01(_baseDistMax * k));
        m.SetFloat("_DistortionMinShadowEnemies", Mathf.Clamp01(_baseDistShadowMin * k));
        m.SetFloat("_DistortionMaxShadowEnemies", Mathf.Clamp01(_baseDistShadowMax * k));
    }

    // ---------- prewarm: find the material and draw it once, hidden, soon after the drop (no hitch on first use) ----------

    private static float _prewarmAt = -1f, _prewarmEnd;
    private static bool _prewarmed, _thermalPrewarming;

    private static void PrewarmThermal()
    {
        if (_thermalPrewarming && Time.time >= _prewarmEnd) { Kill(); ModInfo.Log.LogInfo("[Thermal] prewarmed"); }
        if (_prewarmed) return;
        if (_prewarmAt < 0f) { _prewarmAt = Time.time + 1.5f; return; }
        if (Time.time < _prewarmAt) return;
        _prewarmed = true;
        if (_thermalQuad != null) return; // already switched on by hand
        _thermalPrewarming = true;
        _prewarmEnd = Time.time + 0.3f;
        SetThermal(true);
        if (_thermalQuad == null) _thermalPrewarming = false;
    }

    // ---------- on/off animation: an old monitor — a line opens into the picture, the picture collapses into a line and a dot ----------

    private static float _animAt;
    private static bool _closing;
    private const float OpenTime = 0.2f, CloseTime = 0.25f;

    /// <summary>Picture scale (x, y) for the current moment of the animation; at the end of closing the screen is removed.</summary>
    private static Vector2 AnimScale()
    {
        if (_thermalPrewarming) return new Vector2(0.001f, 0.001f);
        float t = Time.time - _animAt;
        if (!_closing)
        {
            float k = Mathf.Clamp01(t / OpenTime);
            float e = 1f - (1f - k) * (1f - k) * (1f - k); // ease out
            return new Vector2(Mathf.Lerp(0.85f, 1f, e), Mathf.Lerp(0.01f, 1f, e));
        }
        float c = Mathf.Clamp01(t / CloseTime);
        if (c >= 1f) { Kill(); return Vector2.zero; }
        return c < 0.6f
            ? new Vector2(1f, Mathf.Lerp(1f, 0.01f, c / 0.6f))       // collapse to a line
            : new Vector2(Mathf.Lerp(1f, 0.003f, (c - 0.6f) / 0.4f), 0.01f); // line shrinks to a dot
    }

    private static void Kill()
    {
        if (_thermalQuad != null) UnityEngine.Object.Destroy(_thermalQuad);
        _thermalQuad = null; _thermalCam = null; _thermalOn = false; _closing = false; _thermalPrewarming = false;
    }

    private static float _nextTune, _zoom, _thermalRetryAt;
    private static bool _thermalLobbyDone;

    /// <summary>
    /// In the lobby (weapon parts are loaded there): keep a copy of a thermal sight part, so its lens material stays
    /// in memory, and make our thermal material from it — no search in the level, works on any level.
    /// </summary>
    internal static void ThermalLobbyPrep()
    {
        if (_thermalLobbyDone || _thermalMat != null) return;
        _thermalLobbyDone = true;
        try
        {
            var part = TurretVariants.KeptModel("Sight_19_t,Sight_10_t");
            _thermalSearched = false;
            var m = ThermalMaterial();
            ModInfo.Log.LogInfo($"[Thermal] lobby: sight part {(part != null ? "kept" : "not loaded")}, material {(m != null ? m.name : "not found")}");
            if (m != null) FindPalettes(); // the texture search once, here in the lobby (not in the level)
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Thermal] lobby: {e.Message}"); }
    }
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

    private static void SetThermal(bool on, bool instant = false)
    {
        if (!on)
        {
            if (_thermalQuad == null || instant) { Kill(); return; }
            _thermalOn = false; _closing = true; _animAt = Time.time;
            try { CellSound.Post(AK.EVENTS.PRECISIONZOOMOUT, GameUtil.LocalAgent.Position); } catch { }
            ModInfo.Log.LogInfo("[Thermal] off");
            return;
        }
        if (_thermalQuad != null)
        {
            if (_thermalPrewarming) { _thermalPrewarming = false; Kill(); }
            else { _closing = false; _thermalOn = true; _animAt = Time.time; return; } // switched back on while closing
        }
        var agent = GameUtil.LocalAgent;
        var fps = agent?.FPSCamera;
        if (fps == null) { ModInfo.Log.LogWarning("[Thermal] no player camera"); return; }
        var mat = ThermalMaterial();
        if (mat == null) { ModInfo.Log.LogWarning("[Thermal] no thermal material found (the thermal sight parts weren't loaded in the lobby)"); return; }

        var cam = fps.m_itemCamera != null ? fps.m_itemCamera : fps.GetComponent<Camera>(); // the weapon camera, like the sight lens
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
        _thermalQuad = quad; _thermalCam = cam; _thermalOn = !_thermalPrewarming; _closing = false; _animAt = Time.time;
        Fit();
        Tune(mat);
        if (_thermalPrewarming) return;
        try { CellSound.Post(AK.EVENTS.PRECISIONZOOMIN, agent.Position); } catch { }
        ModInfo.Log.LogInfo($"[Thermal] on: material '{mat.name}' (shader '{mat.shader?.name}'), camera '{cam.name}' (fov {cam.fieldOfView:0}, near {cam.nearClipPlane:0.###}), layer {layer} '{LayerMask.LayerToName(layer)}'");
    }

    /// <summary>Scale the quad to cover the camera's whole view (FOV changes when aiming).</summary>
    private static void Fit()
    {
        if (_thermalQuad == null || _thermalCam == null) { Kill(); return; }
        float d = Math.Max(_thermalCam.nearClipPlane + 0.01f, _thermalDistance.Value);
        float h = 2f * d * Mathf.Tan(_thermalCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        float w = h * Math.Max(0.1f, _thermalCam.aspect);
        _thermalQuad.transform.localPosition = new Vector3(0f, 0f, d);
        _thermalQuad.transform.localRotation = Quaternion.identity;
        var a = AnimScale();
        if (_thermalQuad == null) return; // closed just now
        if (_thermalOn && Time.time < _dropUntil) a = new Vector2(a.x, a.y * 0.02f); // signal drop: the picture snaps to a line
        _thermalQuad.transform.localScale = new Vector3(w * a.x, h * a.y, 1f);
    }

    private static int FirstLayerIn(int mask)
    {
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    private static int _lensLayer = -1;

    private static bool Matches(string s, string word) => !string.IsNullOrEmpty(s) && s.ToLowerInvariant().Contains(word);

    /// <summary>The lens material of a thermal sight part, or else any material whose shader / name mentions "thermal".</summary>
    private static Material ThermalMaterial()
    {
        if (_thermalMat != null) return _thermalMat;
        if (_thermalSearched && Time.time < _thermalRetryAt) return null; // the search is a scene-wide scan — not too often
        _thermalSearched = true;
        _thermalRetryAt = Time.time + 20f;
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
                    bool ok = Matches(m.name, "thermal") || Matches(m.shader?.name, "thermal");
                    if (ok && pick == null) { pick = m; _lensLayer = r.gameObject.layer; }
                }
        }
        // 2) Any loaded material.
        if (pick == null)
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
            {
                var m = o?.TryCast<Material>();
                if (m == null) continue;
                bool ok = Matches(m.shader?.name, "thermal") || Matches(m.name, "thermal");
                if (ok) { pick = m; break; }
            }
        if (pick != null)
        {
            _thermalMat = new Material(pick) { name = pick.name + " (GtfoPunk thermal)", hideFlags = HideFlags.DontUnloadUnusedAsset };
            ModInfo.Log.LogInfo($"[Thermal] using material '{pick.name}', shader '{pick.shader?.name}', lens layer {_lensLayer}");
        }
        return _thermalMat;
    }
}
