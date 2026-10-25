using System.Text.RegularExpressions;
using GameData;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Caps the alarm class of security door scans: an alarm puzzle keeps at most MaxAlarmClass scans
/// ("Class V Alarm" with five scans becomes "Class II Alarm" with two). Non-alarm scans are left alone.
/// Changes the level data, so everyone needs the same setting — the host picks it in the lobby (PartyRules panel).
/// </summary>
public class DoorAlarms : ModuleBase
{
    public override string Id => "DoorAlarms";
    public override string Description => "Caps the alarm class of door scans (Class V → Class II by default). Everyone needs it, with the same setting.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<int> _maxClass;

    protected override void OnBindConfig()
    {
        _maxClass = Bind("MaxAlarmClass", 2, "Most scans an alarm door may ask for (1–5). 0 — no cap (as in the game). The host can change it in the lobby (rules panel); the host's value is used for everyone.");
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Snapshot;
        PartyRules.Register("DoorAlarms.MaxAlarmClass", () => Lang.T("Alarm", "Тревога"),
            v => v <= 0 ? Lang.T("default", "как в игре") : Lang.T("level ", "уровень ") + Roman(v), 0, 5, _maxClass, Apply);
    }

    private static readonly Regex ClassName = new(@"Class\s+([IVX]+)", RegexOptions.IgnoreCase);

    /// <summary>The game's own alarm puzzles, kept so the cap can be changed (and lifted) in the lobby.</summary>
    private class Original
    {
        public ChainedPuzzleDataBlock Block;
        public List<ChainedPuzzleComponent> Steps;
        public string Name;
    }

    private static readonly List<Original> _originals = new();
    private static int _applied = -1;

    private static void Snapshot()
    {
        if (_originals.Count > 0) return;
        foreach (var block in GameDataBlockBase<ChainedPuzzleDataBlock>.GetAllBlocks())
        {
            if (block == null || !block.TriggerAlarmOnActivate || block.ChainedPuzzle == null) continue;
            var steps = new List<ChainedPuzzleComponent>();
            foreach (var c in block.ChainedPuzzle) steps.Add(c);
            _originals.Add(new Original { Block = block, Steps = steps, Name = block.PublicAlarmName });
        }
        ModInfo.Log.LogInfo($"[DoorAlarms] alarm puzzles in the game data: {_originals.Count}");
        Apply(_maxClass.Value);
    }

    /// <summary>Restores the game's puzzles, then caps them to max scans (0 — no cap). Takes effect from the next level generation.</summary>
    private static void Apply(int max)
    {
        if (_originals.Count == 0 || max == _applied) return;
        _applied = max;
        int capped = 0;
        foreach (var o in _originals)
        {
            var list = o.Block.ChainedPuzzle;
            list.Clear();
            foreach (var c in o.Steps) list.Add(c);
            o.Block.PublicAlarmName = o.Name;
            if (max <= 0 || o.Steps.Count <= max) continue;
            while (list.Count > max) list.RemoveAt(list.Count - 1);
            if (!string.IsNullOrEmpty(o.Name)) o.Block.PublicAlarmName = ClassName.Replace(o.Name, "Class " + Roman(max), 1);
            capped++;
        }
        ModInfo.Log.LogInfo(max <= 0 ? "[DoorAlarms] no cap — alarms as in the game" : $"[DoorAlarms] alarms capped to {max} scans: {capped} of {_originals.Count} puzzles");
    }

    private static string Roman(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return n >= 0 && n < r.Length ? r[n] : n.ToString();
    }
}
