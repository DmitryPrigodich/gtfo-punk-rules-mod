using Enemies;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Bio-tracker gadget: a small tracker screen above the gadget line (drawn by Loadout.GadgetHud), styled after the
/// game's bio-tracker screen: dark glass, a half-circle with range rings and spokes, glowing blips. Every sweep a pulse
/// runs out from you and shows the enemies within range (sleeping — dimmer), with the distance to the nearest one.
/// It only shows: it doesn't tag enemies, so it gives no bonuses to sentries. Runs on the gadget battery.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<float> _bioRange, _bioSweep;
    private static ConfigEntry<bool> _bioWalls, _bioSound;

    private void BindRadar()
    {
        _bioRange = BindMoved(SecBio, "Range", 20f, "Range of the screen (m).", "BioRange");
        _bioWalls = BindMoved(SecBio, "ThroughWalls", true, "The screen sees through walls.", "BioThroughWalls");
        _bioSound = BindMoved(SecBio, "Sound", true, "Beep on a sweep that finds enemies.", "BioSound");
        _bioSweep = BindMoved(SecBio, "SweepSeconds", 1.5f, "Time between sweeps (s).", "BioSweepSeconds");
    }

    private struct Blip { public Vector3 Pos; public bool Awake; }

    private static bool _radarOn;
    private static float _sweepAt = -10f, _nearest = -1f;
    private static readonly List<Blip> _blips = new();
    private static int _radarLogs;

    private static void RadarTick(bool inLevel, bool perk)
    {
        if (!inLevel || !perk) { if (_radarOn) SetRadar(false, quiet: true); return; }
        if (GadgetKeyDown() && !ChatOpen())
        {
            if (_radarOn) SetRadar(false);
            else if (CanSwitchOn()) SetRadar(true);
        }
        if (_radarOn && Time.time - _sweepAt >= Math.Max(0.3f, _bioSweep.Value)) Sweep();
    }

    private static void SetRadar(bool on, bool quiet = false)
    {
        _radarOn = on;
        _blips.Clear();
        _nearest = -1f;
        if (on)
        {
            _sweepAt = -10f;
        }
        if (quiet) return;
        try { CellSound.Post(on ? AK.EVENTS.PRECISIONZOOMIN : AK.EVENTS.PRECISIONZOOMOUT, GameUtil.LocalAgent.Position); } catch { }
        ModInfo.Log.LogInfo($"[Loadout] bio-tracker screen {(on ? "on" : "off")} (battery {_charge * 100f:0}%)");
    }

    /// <summary>One pulse: who's within range now (no tagging).</summary>
    private static void Sweep()
    {
        _sweepAt = Time.time;
        _blips.Clear();
        _nearest = -1f;
        var agent = GameUtil.LocalAgent;
        if (agent == null || !agent.Alive) return;
        var origin = agent.EyePosition;
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(origin, _bioRange.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var e = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (e == null || !e.Alive || !seen.Add(e.Pointer)) continue;
            if (!_bioWalls.Value && Physics.Linecast(origin, e.EyePosition, LayerManager.MASK_WORLD)) continue;
            bool awake = true;
            try { awake = e.AI == null || e.AI.Mode != Agents.AgentMode.Hibernate; } catch { }
            _blips.Add(new Blip { Pos = e.Position, Awake = awake });
            float d = Vector3.Distance(agent.Position, e.Position);
            if (_nearest < 0f || d < _nearest) _nearest = d;
        }
        if (_blips.Count > 0 && _bioSound.Value) try { CellSound.Post(AK.EVENTS.BIOTRACKER_TAG, origin); } catch { }
        if (_blips.Count > 0 && _radarLogs++ < 5) ModInfo.Log.LogInfo($"[Loadout] bio-tracker screen: {_blips.Count} within {_bioRange.Value:0} m, nearest {_nearest:0.0} m");
    }

    // ---------- drawing (called by the gadget panel, GUI pixels) ----------

    private static Texture2D _texFan, _texBlip, _texWhite;

    /// <summary>The tracker screen in the given rect.</summary>
    private static void DrawRadar(Rect panel, float alpha, GUIStyle small)
    {
        var agent = GameUtil.LocalAgent;
        var cam = agent?.FPSCamera;
        if (agent == null || cam == null) return;
        MakeRadarTextures();
        var line = new Color(0.78f, 0.9f, 0.95f);
        Box(panel, new Color(0.02f, 0.05f, 0.08f, 0.72f * alpha), new Color(line.r, line.g, line.b, 0.6f * alpha));

        float pad = panel.height * 0.08f;
        float R = Math.Min(panel.width * 0.5f - pad, panel.height - pad * 2.4f);
        float cx = panel.center.x, by = panel.yMax - pad * 1.2f;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(cx - R, by - R, 2f * R, R), _texFan);

        // The pulse running outwards.
        float sweep = Math.Max(0.3f, _bioSweep.Value);
        float p = Mathf.Clamp01((Time.time - _sweepAt) / (sweep * 0.6f));
        if (p < 1f)
        {
            float r = R * p;
            GUI.color = new Color(line.r, line.g, line.b, alpha * 0.5f * (1f - p));
            GUI.DrawTexture(new Rect(cx - r, by - r, 2f * r, r), _texFan);
        }

        // Blips in the view's frame: right → x, forward → up.
        var fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = agent.transform.forward;
        fwd.Normalize();
        var right = new Vector3(fwd.z, 0f, -fwd.x);
        float range = Math.Max(1f, _bioRange.Value);
        float age = Mathf.Clamp01((Time.time - _sweepAt) / sweep);
        foreach (var b in _blips)
        {
            var d = b.Pos - agent.Position;
            float x = Vector3.Dot(d, right) / range, y = Vector3.Dot(d, fwd) / range;
            if (y < 0f || x * x + y * y > 1f) continue; // behind you or out of range
            if (p < 1f && Mathf.Sqrt(x * x + y * y) > p) continue; // appears when the pulse reaches it
            float fade = Mathf.Lerp(1f, 0.45f, age);
            // Like the tool's blips: a dot, an iris and an outer ring. Awake enemies turn red and blink.
            float sz = R * (b.Awake ? 0.26f : 0.2f);
            float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 9f);
            GUI.color = b.Awake ? new Color(1f, 0.18f, 0.12f, alpha * fade * blink) : new Color(0.92f, 0.96f, 1f, alpha * fade * 0.85f);
            GUI.DrawTexture(new Rect(cx + x * R - sz * 0.5f, by - y * R - sz * 0.5f, sz, sz), _texBlip);
        }

        // Range marks along the base and the readouts, like the tool's screen.
        GUI.color = new Color(line.r, line.g, line.b, alpha * 0.9f);
        string far = $"{range:0}", mid = $"{range * 0.5f:0}";
        float lw = R * 0.3f, lh = small.fontSize * 1.3f;
        small.alignment = TextAnchor.UpperCenter;
        GUI.Label(new Rect(cx - R - lw * 0.5f, by + 1f, lw, lh), far, small);
        GUI.Label(new Rect(cx + R - lw * 0.5f, by + 1f, lw, lh), far, small);
        GUI.Label(new Rect(cx - R * 0.5f - lw * 0.5f, by + 1f, lw, lh), mid, small);
        GUI.Label(new Rect(cx + R * 0.5f - lw * 0.5f, by + 1f, lw, lh), mid, small);
        small.alignment = TextAnchor.UpperLeft;
        GUI.Label(new Rect(panel.x + pad, panel.y + pad * 0.5f, panel.width * 0.5f, lh), Lang.T("TARGETS", "ЦЕЛИ"), small);
        int awake = 0;
        foreach (var b in _blips) if (b.Awake) awake++;
        int oldSize = small.fontSize;
        small.fontSize = oldSize * 2;
        GUI.color = awake > 0 ? new Color(1f, 0.25f, 0.2f, alpha) : new Color(line.r, line.g, line.b, alpha);
        GUI.Label(new Rect(panel.x + pad, panel.y + pad * 0.5f + lh * 0.9f, panel.width * 0.4f, oldSize * 2.6f), $"{_blips.Count}", small);
        small.fontSize = oldSize;
        GUI.color = new Color(line.r, line.g, line.b, alpha * 0.9f);
        small.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(panel.center.x, panel.y + pad * 0.5f, panel.width * 0.5f - pad, lh), _nearest >= 0f ? $"{_nearest:0.0} m" : "--.- m", small);
    }

    /// <summary>A filled rect with a 1 px frame.</summary>
    private static void Box(Rect r, Color fill, Color frame)
    {
        _texWhite ??= MakeWhite();
        GUI.color = fill;
        GUI.DrawTexture(r, _texWhite);
        GUI.color = frame;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), _texWhite);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), _texWhite);
        GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), _texWhite);
        GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), _texWhite);
    }

    private static void MakeRadarTextures()
    {
        if (_texFan != null && _texBlip != null) return;
        const int W = 512, H = 256;
        static float Line(float dist, float width) => Mathf.Clamp01(1f - Math.Abs(dist) / width);

        // Lines only (white; tinted when drawn): outer and middle arcs, a small inner gauge arc, two spokes, the base.
        _texFan = NewTex(W, H);
        var px = new Color[W * H];
        float edge = H - 2f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = x - W * 0.5f + 0.5f, dy = y + 0.5f;
                float rr = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float a = 0f;
                if (rr <= edge + 2f)
                {
                    a = Math.Max(a, Line(rr - edge, 1.6f) * 0.95f);
                    a = Math.Max(a, Line(rr - edge * 0.5f, 1.3f) * 0.6f);
                    if (ang > 30f && ang < 150f) a = Math.Max(a, Line(rr - edge * 0.12f, 2.5f) * 0.8f);
                    foreach (var spoke in new[] { 45f, 135f })
                        if (rr > edge * 0.55f && rr < edge * 0.9f) a = Math.Max(a, Line((ang - spoke) * Mathf.Deg2Rad * rr, 1.6f) * 0.85f);
                    a = Math.Max(a, Line(dy - 0.5f, 1.2f) * 0.5f);
                    a = Math.Max(a, 0.06f * (1f - rr / edge)); // faint glow towards the centre
                }
                px[y * W + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        _texFan.SetPixels(px);
        _texFan.Apply();

        const int B = 64;
        _texBlip = NewTex(B, B);
        var bp = new Color[B * B];
        for (int y = 0; y < B; y++)
            for (int x = 0; x < B; x++)
            {
                float dx = (x + 0.5f) / B * 2f - 1f, dy = (y + 0.5f) / B * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (r < 0.16f) a = 1f;                                   // the dot
                else if (r > 0.3f && r < 0.5f) a = 0.28f;                 // the iris
                a = Math.Max(a, Mathf.Clamp01(1f - Math.Abs(r - 0.5f) / 0.04f) * 0.95f); // iris edge
                a = Math.Max(a, Mathf.Clamp01(1f - Math.Abs(r - 0.82f) / 0.05f) * 0.8f); // outer ring
                bp[y * B + x] = new Color(1f, 1f, 1f, a);
            }
        _texBlip.SetPixels(bp);
        _texBlip.Apply();
    }

    private static Texture2D MakeWhite()
    {
        var t = NewTex(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        return t;
    }

    private static Texture2D NewTex(int w, int h) =>
        new(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
}
