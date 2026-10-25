using HarmonyLib;
using UnityEngine;

namespace GtfoPunk.Core;

/// <summary>
/// Adds a line to the game's version watermark (bottom right, "CORTEX NI V1.0 …"), e.g. "GPUNK RULES v0.9.7",
/// so screenshots show which mods are running. Each mod adds its own line right after the game rewrites the text
/// (a postfix on PUI_Watermark.UpdateWatermark), plus a cheap check once a second in case it was rewritten elsewhere.
/// </summary>
public static class Watermark
{
    private static string _line;
    private static bool _hooked;
    private static PUI_Watermark _wm;
    private static float _next, _nextFind;

    public static void Register(string line)
    {
        _line = line;
        if (_hooked) return;
        _hooked = true;
        try
        {
            new Harmony(ModInfo.Guid + ".Watermark").Patch(AccessTools.Method(typeof(PUI_Watermark), nameof(PUI_Watermark.UpdateWatermark)),
                postfix: new HarmonyMethod(typeof(Watermark), nameof(Postfix)));
        }
        catch (System.Exception e) { ModInfo.Log.LogWarning($"[Watermark] {e.Message}"); }
        FrameTicker.OnUpdate += Tick;
    }

    private static void Postfix(PUI_Watermark __instance)
    {
        _wm = __instance;
        Append(__instance);
    }

    private static void Append(PUI_Watermark wm)
    {
        var t = wm?.m_watermarkText;
        if (t == null || _line == null) return;
        string text = t.text ?? "";
        if (text.Contains(_line)) return;
        t.SetText(text + "\n<size=75%>" + _line + "</size>");
        if (!_logged) { _logged = true; ModInfo.Log.LogInfo($"[Watermark] added '{_line}' (overflow {t.overflowMode}, wrap {t.enableWordWrapping})"); }
        // Room for the extra lines: no clipping of the text box.
        t.overflowMode = TMPro.TextOverflowModes.Overflow;
    }

    private static bool _logged;

    private static void Tick()
    {
        if (_line == null || Time.time < _next) return;
        _next = Time.time + 1f;
        // The watermark object lives for the whole session: find it once (a scene search is a hitch), not every check.
        if (_wm == null)
        {
            if (Time.time < _nextFind) return;
            _nextFind = Time.time + 15f;
            _wm = UnityEngine.Object.FindObjectOfType<PUI_Watermark>();
        }
        Append(_wm);
    }
}
