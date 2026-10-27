using System.Text.RegularExpressions;
using GameData;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Caps the alarm class of security doors by the party difficulty (module Difficulty): an alarm puzzle keeps at most
/// N scans ("Class V Alarm" with five scans becomes "Class II Alarm" with two); at level I (N = 0) an alarm becomes a
/// plain one-scan security check without a wave. DEFAULT — as in the game. Non-alarm scans are left alone.
/// Changes the level data, so everyone needs the same setting (the host's is sent to everyone).
/// </summary>
public class DoorAlarms : ModuleBase
{
    public override string Id => "DoorAlarms";
    public override string Description => "Caps the alarm class of door scans by the party difficulty (level I — one scan, no alarm). Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<bool> _keepScripted;

    protected override void OnBindConfig()
    {
        _keepScripted = Bind("KeepScriptedDoors", true, "Doors with scripted scans (events on scan start / done), apex and checkpoint doors keep the game's own alarm — changing them can break the level's script.");
        ConfigMigration.DropOrphans(Config, Id); // MaxAlarmClass → Difficulty (after binding — otherwise it drops the keys above)
    }

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
        int kept = 0;
        var keptNames = new List<string>();
        var copies = new Dictionary<uint, uint>();
        foreach (var layout in GameDataBlockBase<LevelLayoutDataBlock>.GetAllBlocks())
        {
            if (layout?.Zones == null) continue;
            foreach (var z in layout.Zones)
            {
                if (z == null || !_alarmIds.Contains(z.ChainedPuzzleToEnter)) continue;
                if (_keepScripted.Value && Scripted(z))
                {
                    // Its own untouched copy of the alarm, so capping the shared puzzle doesn't reach this door.
                    uint orig = z.ChainedPuzzleToEnter;
                    if (!copies.TryGetValue(orig, out var copy)) copies[orig] = copy = KeptCopy(orig);
                    if (copy != 0) { z.ChainedPuzzleToEnter = copy; kept++; if (keptNames.Count < 12) keptNames.Add($"{layout.name}/{z.LocalIndex}"); continue; }
                }
                _alarmZones.Add((z, z.ChainedPuzzleToEnter));
            }
        }
        ModInfo.Log.LogInfo($"[DoorAlarms] alarm puzzles in the game data: {_originals.Count}, alarm doors in level layouts: {_alarmZones.Count}; "
            + $"scripted doors kept as in the game: {kept}{(keptNames.Count > 0 ? " (" + string.Join(", ", keptNames) + (kept > keptNames.Count ? ", …" : "") + ")" : "")}");
        Apply(Difficulty.AlarmCap);
    }

    /// <summary>A door the level's script hooks into: events on its scan, an apex gate, a checkpoint door.</summary>
    private static bool Scripted(ExpeditionZoneData z)
    {
        try
        {
            return (z.EventsOnDoorScanStart?.Count ?? 0) > 0 || (z.EventsOnDoorScanDone?.Count ?? 0) > 0
                || z.SecurityGateToEnter == eSecurityGateType.Apex || z.IsCheckpointDoor;
        }
        catch { return false; }
    }

    /// <summary>An untouched copy of an alarm puzzle (its own step list), registered under a stable new ID.</summary>
    private static uint KeptCopy(uint origId)
    {
        try
        {
            var src = GameDataBlockBase<ChainedPuzzleDataBlock>.GetBlock(origId);
            if (src == null) return 0;
            string key = "GtfoPunk_KeptAlarm_" + origId;
            if (GameDataBlockBase<ChainedPuzzleDataBlock>.HasBlock(key)) return GameDataBlockBase<ChainedPuzzleDataBlock>.GetBlockID(key);
            var c = GearFactory.CloneBlock(src);
            var steps = new Il2CppSystem.Collections.Generic.List<ChainedPuzzleComponent>();
            foreach (var st in src.ChainedPuzzle) steps.Add(st);
            c.ChainedPuzzle = steps;
            c.persistentID = GearFactory.StableId<ChainedPuzzleDataBlock>(key);
            c.name = key;
            GameDataBlockBase<ChainedPuzzleDataBlock>.AddBlock(c);
            return c.persistentID;
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[DoorAlarms] copy of puzzle {origId}: {e.Message}"); return 0; }
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
            o.Block.TriggerAlarmOnActivate = true;
            if (max == 0)
            {
                // Level I: a plain one-scan security check instead of the alarm (no wave). The door keeps its puzzle,
                // so scripted doors that wait for "scan done" still open.
                while (list.Count > 1) list.RemoveAt(list.Count - 1);
                o.Block.TriggerAlarmOnActivate = false;
                capped++;
                continue;
            }
            if (max < 1 || o.Steps.Count <= max) continue;
            while (list.Count > max) list.RemoveAt(list.Count - 1);
            if (!string.IsNullOrEmpty(o.Name)) o.Block.PublicAlarmName = ClassName.Replace(o.Name, "Class " + Roman(max), 1);
            capped++;
        }
        foreach (var (zone, puzzle) in _alarmZones) zone.ChainedPuzzleToEnter = puzzle; // (0.9.7 removed the puzzle at level I — that broke scripted doors)
        ModInfo.Log.LogInfo(max < 0 ? "[DoorAlarms] alarms as in the game"
            : max == 0 ? $"[DoorAlarms] alarms turned into plain one-scan checks: {capped} puzzles"
            : $"[DoorAlarms] alarms capped to {max} scans: {capped} of {_originals.Count} puzzles");
    }

    private static string Roman(int n)
    {
        string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
        return n >= 0 && n < r.Length ? r[n] : n.ToString();
    }
}
