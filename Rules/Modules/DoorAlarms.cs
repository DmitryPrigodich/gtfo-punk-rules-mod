using System.Text.RegularExpressions;
using GameData;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Caps the alarm class of security door scans: an alarm puzzle keeps at most MaxAlarmClass scans
/// ("Class V Alarm" with five scans becomes "Class II Alarm" with two). Non-alarm scans are left alone.
/// Changes the level data, so everyone needs the same setting.
/// </summary>
public class DoorAlarms : ModuleBase
{
    public override string Id => "DoorAlarms";
    public override string Description => "Caps the alarm class of door scans (Class V → Class II by default). Everyone needs it, with the same setting.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<int> _maxClass;

    protected override void OnBindConfig()
    {
        _maxClass = Bind("MaxAlarmClass", 2, "Most scans an alarm door may ask for (1, 2, 3…). 0 — no cap (as in the game).");
    }

    protected override void OnEnable() => GearFactory.OnInject += Apply;

    private static bool _applied;
    private static readonly Regex ClassName = new(@"Class\s+([IVX]+)", RegexOptions.IgnoreCase);

    private static void Apply()
    {
        if (_applied) return;
        _applied = true;
        int max = _maxClass.Value;
        if (max <= 0) { ModInfo.Log.LogInfo("[DoorAlarms] no cap (MaxAlarmClass = 0)"); return; }
        int capped = 0, total = 0;
        foreach (var block in GameDataBlockBase<ChainedPuzzleDataBlock>.GetAllBlocks())
        {
            if (block == null || !block.TriggerAlarmOnActivate || block.ChainedPuzzle == null) continue;
            total++;
            int count = block.ChainedPuzzle.Count;
            if (count <= max) continue;
            while (block.ChainedPuzzle.Count > max) block.ChainedPuzzle.RemoveAt(block.ChainedPuzzle.Count - 1);
            string oldName = block.PublicAlarmName;
            if (!string.IsNullOrEmpty(oldName))
                block.PublicAlarmName = ClassName.Replace(oldName, "Class " + Roman(max), 1);
            capped++;
            if (capped <= 10) ModInfo.Log.LogInfo($"[DoorAlarms] {block.name}: {count} scans → {max} ('{oldName}' → '{block.PublicAlarmName}')");
        }
        ModInfo.Log.LogInfo($"[DoorAlarms] alarm puzzles: {total}, capped to {max} scans: {capped}");
    }

    private static string Roman(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return n >= 0 && n < r.Length ? r[n] : n.ToString();
    }
}
