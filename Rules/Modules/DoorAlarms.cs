using System.Text.RegularExpressions;
using GameData;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Caps the alarm class of security doors by the party difficulty (module Difficulty): an alarm puzzle keeps at most
/// N scans ("Class V Alarm" with five scans becomes "Class II Alarm" with two); at level I (N = 0) alarm doors have no
/// puzzle at all and just open. DEFAULT — as in the game. Non-alarm scans are left alone.
/// Changes the level data, so everyone needs the same setting (the host's is sent to everyone).
/// </summary>
public class DoorAlarms : ModuleBase
{
    public override string Id => "DoorAlarms";
    public override string Description => "Caps the alarm class of door scans by the party difficulty (level I — alarm doors just open). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    protected override void OnBindConfig() => ConfigMigration.DropOrphans(Config, Id); // MaxAlarmClass → Difficulty

    protected override void OnEnable()
    {
        GearFactory.OnInject += Snapshot;
        Difficulty.Changed += _ => Apply(Difficulty.AlarmCap);
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
    private static readonly HashSet<uint> _alarmIds = new();
    /// <summary>Zones whose security door uses an alarm puzzle, with that puzzle's ID (cleared at cap 0).</summary>
    private static readonly List<(ExpeditionZoneData Zone, uint Puzzle)> _alarmZones = new();
    private static int _applied = int.MinValue;

    private static void Snapshot()
    {
        if (_originals.Count > 0) return;
        foreach (var block in GameDataBlockBase<ChainedPuzzleDataBlock>.GetAllBlocks())
        {
            if (block == null || !block.TriggerAlarmOnActivate || block.ChainedPuzzle == null) continue;
            var steps = new List<ChainedPuzzleComponent>();
            foreach (var c in block.ChainedPuzzle) steps.Add(c);
            _originals.Add(new Original { Block = block, Steps = steps, Name = block.PublicAlarmName });
            _alarmIds.Add(block.persistentID);
        }
        foreach (var layout in GameDataBlockBase<LevelLayoutDataBlock>.GetAllBlocks())
        {
            if (layout?.Zones == null) continue;
            foreach (var z in layout.Zones)
                if (z != null && _alarmIds.Contains(z.ChainedPuzzleToEnter)) _alarmZones.Add((z, z.ChainedPuzzleToEnter));
        }
        ModInfo.Log.LogInfo($"[DoorAlarms] alarm puzzles in the game data: {_originals.Count}, alarm doors in level layouts: {_alarmZones.Count}");
        Apply(Difficulty.AlarmCap);
    }

    /// <summary>Restores the game's puzzles, then caps them to max scans (-1 — as in the game; 0 — no puzzle on alarm doors).</summary>
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
            if (max < 1 || o.Steps.Count <= max) continue;
            while (list.Count > max) list.RemoveAt(list.Count - 1);
            if (!string.IsNullOrEmpty(o.Name)) o.Block.PublicAlarmName = ClassName.Replace(o.Name, "Class " + Roman(max), 1);
            capped++;
        }
        foreach (var (zone, puzzle) in _alarmZones) zone.ChainedPuzzleToEnter = max == 0 ? 0u : puzzle;
        ModInfo.Log.LogInfo(max < 0 ? "[DoorAlarms] alarms as in the game"
            : max == 0 ? $"[DoorAlarms] alarm doors without a puzzle: {_alarmZones.Count}"
            : $"[DoorAlarms] alarms capped to {max} scans: {capped} of {_originals.Count} puzzles");
    }

    private static string Roman(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return n >= 0 && n < r.Length ? r[n] : n.ToString();
    }
}
