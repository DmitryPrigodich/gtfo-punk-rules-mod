using Enemies;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Bio-tracker gadget: a motion-tracker screen at the bottom of the view (the idea of the Aliens vs Predator tracker,
/// the look of GTFO's bio-tracker screen: dark glass, pale lines, red blips).
/// A half-circle facing where you look; every sweep a pulse runs out from you and shows the enemies within range as
/// blips (sleeping — dim, awake — bright), with the distance to the nearest one. It only shows: it doesn't tag
/// enemies, so it gives no bonuses to sentries. Runs on the gadget battery.
/// </summary>
public partial class Loadout
{
    private static ConfigEntry<float> _bioRange, _bioSweep, _radarSize, _radarOpacity, _radarBottom;
    private static ConfigEntry<bool> _bioWalls, _bioSound;
    private static ConfigEntry<string> _radarPlace, _radarShift;

    private void BindRadar()
    {
        _bioRange = BindMigrated(Id, "BioRange", 20f, "Bio-tracker screen: range (m).", (Id, "PerkScanRange", "15"));
        _bioWalls = BindMigrated(Id, "BioThroughWalls", true, "Bio-tracker screen sees through walls.", (Id, "PerkThroughWalls", "true"));
        _bioSound = BindMigrated(Id, "BioSound", true, "Bio-tracker screen beeps on a sweep that finds enemies.", (Id, "PerkSound", "true"));
        _bioSweep = Bind("BioSweepSeconds", 1.5f, "Bio-tracker screen: time between sweeps (s).");
        _radarSize = BindMigrated(Id, "BioScreenRadius", 0.12f, "Bio-tracker screen radius, as a fraction of the screen height.", (Id, "BioScreenSize", "0.2"));
        _radarPlace = Bind("BioScreenPlace", "health", "Where the bio-tracker screen sits: health — above the health readout, in place of the booster icons; bottom — bottom centre of the view.");
        _radarShift = Bind("BioScreenShift", "0,0", "Shift of the bio-tracker screen from its place (x right, y up; fractions of the screen height).");
        _radarOpacity = Bind("BioScreenOpacity", 0.65f, "Bio-tracker screen opacity (0–1).");
        _radarBottom = Bind("BioScreenBottom", 0.02f, "BioScreenPlace = bottom: gap between the screen and the bottom of the view (fraction of the screen height).");
    }

    private struct Blip { public Vector3 Pos; public bool Awake; }

    private static bool _radarOn, _radarHooked;
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
        if (_radarOn) FindAnchor();
    }

    // ---------- where to draw: above the health readout (in GUI pixels) ----------

    private static bool _anchorOk;
    private static Vector2 _anchor; // GUI space (y from the top): middle of the line the screen stands on
    private static Camera _hudCam;
    private static PUI_LocalPlayerStatus _radarStatus;

    private static void FindAnchor()
    {
        _anchorOk = false;
        if (!string.Equals(_radarPlace.Value, "health", StringComparison.OrdinalIgnoreCase)) return;
        // Stand on the gadget line (it's where the booster icons were); without it — on the health readout.
        TMPro.TextMeshPro t = _gadgetText != null && _gadgetText.gameObject.activeInHierarchy ? _gadgetText : null;
        if (t == null)
        {
            if (_radarStatus == null) _radarStatus = UnityEngine.Object.FindObjectOfType<PUI_LocalPlayerStatus>();
            t = _radarStatus?.m_healthText;
        }
        if (t == null) return;
        _hudCam = UiCamera.For(t.gameObject.layer);
        if (_hudCam == null) return;
        var r = t.GetComponent<Renderer>();
        if (r == null) return;
        var b = r.bounds;
        var top = _hudCam.WorldToScreenPoint(new Vector3(b.center.x, b.max.y, b.center.z));
        if (top.z < 0f) return;
        _anchor = new Vector2(top.x, Screen.height - top.y);
        _anchorOk = true;
    }

    private static void SetRadar(bool on, bool quiet = false)
    {
        _radarOn = on;
        _blips.Clear();
        _nearest = -1f;
        if (on)
        {
            Overlay.Ensure();
            if (!_radarHooked) { _radarHooked = true; Overlay.OnDraw += DrawRadar; }
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

    // ---------- drawing ----------

    private static Texture2D _texFan, _texRing, _texBlip;
    private static GUIStyle _radarLabel;

    private static void DrawRadar()
    {
        if (!_radarOn) return;
        var focus = FocusStateManager.CurrentState;
        if (focus != eFocusState.FPS && focus != eFocusState.FPS_TypingInChat && focus != eFocusState.FPS_CommunicationDialog) return;
        var agent = GameUtil.LocalAgent;
        var cam = agent?.FPSCamera;
        if (agent == null || cam == null || !agent.Alive) return;
        MakeRadarTextures();

        float R = Screen.height * Mathf.Clamp(_radarSize.Value, 0.03f, 0.5f);
        float cx = Screen.width * 0.5f, by = Screen.height * (1f - Mathf.Clamp(_radarBottom.Value, 0f, 0.5f));
        if (_anchorOk) { cx = _anchor.x; by = _anchor.y - R * 0.12f; } // above the gadget line, centered
        var shift = TurretVariants.Vec(_radarShift.Value);
        cx += shift.x * Screen.height;
        by -= shift.y * Screen.height;
        float a = Mathf.Clamp01(_radarOpacity.Value);
        var old = GUI.color;

        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(cx - R, by - R, 2f * R, R), _texFan);

        // The pulse running outwards.
        float sweep = Math.Max(0.3f, _bioSweep.Value);
        float p = Mathf.Clamp01((Time.time - _sweepAt) / (sweep * 0.6f));
        if (p < 1f)
        {
            float r = R * p;
            GUI.color = new Color(1f, 1f, 1f, a * (1f - p));
            GUI.DrawTexture(new Rect(cx - r, by - r, 2f * r, r), _texRing);
        }

        // Blips in the view's frame: right → x, forward → up.
        var fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = agent.transform.forward;
        fwd.Normalize();
        var right = new Vector3(fwd.z, 0f, -fwd.x);
        float range = Math.Max(1f, _bioRange.Value);
        float age = Mathf.Clamp01((Time.time - _sweepAt) / sweep);
        float size = R * 0.085f;
        foreach (var b in _blips)
        {
            var d = b.Pos - agent.Position;
            float x = Vector3.Dot(d, right) / range, y = Vector3.Dot(d, fwd) / range;
            if (y < 0f || x * x + y * y > 1f) continue; // behind you or out of range
            // Appears when the pulse reaches it, then fades towards the next sweep.
            if (p < 1f && Mathf.Sqrt(x * x + y * y) > p) continue;
            float fade = Mathf.Lerp(1f, 0.35f, age);
            GUI.color = b.Awake ? new Color(1f, 0.12f, 0.08f, a * fade) : new Color(0.7f, 0.08f, 0.06f, a * fade * 0.85f);
            float s = b.Awake ? size : size * 0.8f;
            GUI.DrawTexture(new Rect(cx + x * R - s * 0.5f, by - y * R - s * 0.5f, s, s), _texBlip);
        }

        // Distance to the nearest one, AvP style.
        if (_radarLabel == null)
            _radarLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
        _radarLabel.fontSize = Math.Max(10, (int)(R * 0.11f));
        GUI.color = new Color(0.88f, 0.92f, 0.95f, a);
        string text = _nearest >= 0f ? $"{_nearest:00.0}m" : "--.-m";
        GUI.Label(new Rect(cx + R * 0.35f, by - R * 0.22f, R * 0.6f, R * 0.2f), text, _radarLabel);

        // Battery along the base of the screen.
        if (_batterySeconds.Value > 0f)
        {
            _texWhite ??= MakeWhite();
            float bw = 2f * R * 0.9f, bh = Math.Max(2f, R * 0.025f), bx = cx - bw * 0.5f, byy = by + bh * 0.8f;
            GUI.color = new Color(1f, 1f, 1f, a * 0.25f);
            GUI.DrawTexture(new Rect(bx, byy, bw, bh), _texWhite);
            GUI.color = _charge < Mathf.Clamp01(_minCharge.Value) ? new Color(0.9f, 0.3f, 0.25f, a) : _charge < 0.35f ? new Color(0.9f, 0.75f, 0.3f, a) : new Color(0.88f, 0.92f, 0.95f, a);
            GUI.DrawTexture(new Rect(bx, byy, bw * Mathf.Clamp01(_charge), bh), _texWhite);
        }
        GUI.color = old;
    }

    private static void MakeRadarTextures()
    {
        if (_texFan != null && _texRing != null && _texBlip != null) return;
        const int W = 512, H = 256;
        var teal = new Color(0.82f, 0.88f, 0.92f); // pale grey-blue lines, like the bio-tracker screen

        static float Line(float dist, float width) => Mathf.Clamp01(1f - Math.Abs(dist) / width);

        _texFan = NewTex(W, H);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = x - W * 0.5f + 0.5f, dy = y + 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (H - 2f);
                if (r > 1.01f) { px[y * W + x] = Color.clear; continue; }
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // 0 = right, 90 = forward
                float rr = r * (H - 2f);
                float lines = Math.Max(Line(rr - (H - 2f), 1.8f),                                             // rim
                              Math.Max(Line(rr - (H - 2f) / 3f, 1.2f) * 0.6f, Line(rr - (H - 2f) * 2f / 3f, 1.2f) * 0.6f)); // range rings
                foreach (var spoke in new[] { 45f, 90f, 135f })
                    lines = Math.Max(lines, Line((ang - spoke) * Mathf.Deg2Rad * rr, 1f) * 0.35f);
                lines = Math.Max(lines, Line(dy - 0.5f, 1.2f) * 0.8f); // base line
                float fill = 0.35f + 0.15f * (1f - r);
                var c = Color.Lerp(new Color(0.015f, 0.02f, 0.025f, fill + 0.15f), new Color(teal.r, teal.g, teal.b, 0.85f), lines);
                if (r > 1f) c.a *= Mathf.Clamp01((1.01f - r) * 100f);
                px[y * W + x] = c;
            }
        _texFan.SetPixels(px);
        _texFan.Apply();

        _texRing = NewTex(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = x - W * 0.5f + 0.5f, dy = y + 0.5f;
                float rr = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = H - 2f;
                float k = rr > edge ? Line(rr - edge, 2.5f) : Mathf.Pow(Mathf.Clamp01(1f - (edge - rr) / 40f), 2f) * 0.5f + Line(rr - edge, 2.5f) * 0.5f;
                px[y * W + x] = new Color(teal.r, teal.g, teal.b, Mathf.Clamp01(k));
            }
        _texRing.SetPixels(px);
        _texRing.Apply();

        const int B = 32;
        _texBlip = NewTex(B, B);
        var bp = new Color[B * B];
        for (int y = 0; y < B; y++)
            for (int x = 0; x < B; x++)
            {
                float dx = (x + 0.5f) / B * 2f - 1f, dy = (y + 0.5f) / B * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float k = Mathf.Clamp01(1f - r);
                bp[y * B + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(k * k * 1.6f + (r < 0.45f ? 0.4f : 0f)));
            }
        _texBlip.SetPixels(bp);
        _texBlip.Apply();
    }

    private static Texture2D _texWhite;

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
