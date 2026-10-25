using GameData;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Optional measurements of the game's fog repellers (FogDiagnostics): 5 s into each level, to the log and to
/// BepInEx/config/GtfoPunk.FogRepellers.txt. Searches the whole scene — causes a hitch, so off by default.
/// </summary>
public partial class TurretVariants
{
    private static ConfigEntry<bool> _fogDump;
    private static float _fogDumpAt = -1f;
    private static bool _fogDumped;

    private void BindDiagnostics() =>
        _fogDump = Bind("FogDiagnostics", false, "Write fog repeller measurements to GtfoPunk.FogRepellers.txt a few seconds into each level (diagnostics; causes a hitch).");

    private static void ResetDiagnostics() { _fogDumpAt = -1f; _fogDumped = false; }

    private static void DiagnosticsTick()
    {
        if (!_fogDump.Value || _fogDumped || !GameUtil.InLevel) return;
        if (_fogDumpAt < 0f) { _fogDumpAt = Time.time + 5f; return; }
        if (Time.time < _fogDumpAt) return;
        _fogDumped = true;
        try { DumpFogRepellers(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] fog measurement: {e.Message}"); }
    }

    private static string Describe(FogRepeller_Sphere f) =>
        $"radius {f.Range:0.##} m, lasts {(f.InfiniteDuration ? "forever" : f.LifeDuration.ToString("0.#") + " s")}, " +
        $"grows {f.GrowDuration:0.#} s, shrinks {f.ShrinkDuration:0.#} s, density {f.Density:0.##}";

    private static void DumpFogRepellers()
    {
        var lines = new List<string> { $"# Fog repellers ({DateTime.Now:yyyy-MM-dd HH:mm}), level: {RundownManager.ActiveExpedition?.Descriptive?.PublicName}" };
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode != null)
            for (int m = 0; m < perMode.Length; m++)
            {
                var dict = perMode[m];
                if (dict == null) continue;
                foreach (var kv in dict)
                {
                    var list = kv.Value;
                    if (list == null || list.Count == 0 || list[0] == null) continue;
                    foreach (var f in list[0].GetComponentsInChildren<FogRepeller_Sphere>(true))
                        lines.Add($"model: {GameDataBlockBase<ItemDataBlock>.GetBlock(kv.Key)?.publicName ?? kv.Key.ToString()} ({kv.Key}), mode {(ItemMode)m}: {Describe(f)}");
                }
            }
        foreach (var g in UnityEngine.Object.FindObjectsOfType<HeavyFogRepellerGlobalState>())
            if (g != null && g.m_repellerSphere != null) lines.Add($"in level: turbine '{g.gameObject.name}': {Describe(g.m_repellerSphere)}");
        foreach (var f in UnityEngine.Object.FindObjectsOfType<FogRepeller_Sphere>())
            if (f != null) lines.Add($"in level: '{f.gameObject.name}' ({f.CurrentState}, now {f.m_currentRange:0.##} m): {Describe(f)}");
        if (lines.Count == 1) lines.Add("no repellers found (models not loaded on this level)");
        foreach (var l in lines) ModInfo.Log.LogInfo($"[Fog] {l}");
        System.IO.File.AppendAllLines(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.FogRepellers.txt"), lines);
    }
}
