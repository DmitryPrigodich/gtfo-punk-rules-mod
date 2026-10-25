using UnityEngine;

namespace GtfoPunk.Core;

/// <summary>
/// Adds a line to the game's version watermark (bottom right, "CORTEX NI V1.0 …"), e.g. "GPUNK RULES v0.9.6",
/// so screenshots show which mods are running. Each mod adds its own line; checked every few seconds because the
/// game may rewrite the text.
/// </summary>
public static class Watermark
{
    private static string _line;
    private static float _next;
    private static bool _hooked;

    public static void Register(string line)
    {
        _line = line;
        if (_hooked) return;
        _hooked = true;
        FrameTicker.OnUpdate += Tick;
    }

    private static void Tick()
    {
        if (_line == null || Time.time < _next) return;
        _next = Time.time + 3f;
        foreach (var wm in UnityEngine.Object.FindObjectsOfType<PUI_Watermark>())
        {
            var t = wm?.m_watermarkText;
            if (t == null) continue;
            string text = t.text ?? "";
            if (text.Contains(_line)) continue;
            t.SetText(text + "\n<size=75%>" + _line + "</size>");
        }
    }
}
