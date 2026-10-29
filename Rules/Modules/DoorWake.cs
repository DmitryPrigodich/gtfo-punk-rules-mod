using ChainedPuzzles;
using Il2CppInterop.Runtime.InteropTypes;
using Enemies;
using LevelGeneration;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Loud doors. A bioscan starts with the loudspeaker announcing it — that wakes the sleepers around the scan (within
/// WakeRadius, through walls) and stirs those further off (up to StirRadius): the room has to be cleared before the scan
/// can be done in peace. Optional (off by default): the first security door opening (or every one, AllDoors) and the
/// elevator landing make the same noise. Zones behind closed doors don't hear it. The host makes the noise.
/// </summary>
public class DoorWake : ModuleBase
{
    public override string Id => "DoorWake";
    public override string Section => "Level.Doors";
    public override IReadOnlyList<string> OldSections => new[] { "Level.FirstDoor", "Level.Elevator" };
    public override string Description => "A bioscan's loudspeaker wakes the sleepers around it (the room has to be cleared first); optionally the first door opening and the elevator landing too. Host only.";
    public override ModuleSide Side => ModuleSide.Host;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<float> _delay, _wake, _stir;
    private static ConfigEntry<bool> _stirWakes, _all, _scan, _open, _elevator;

    protected override void OnBindConfig()
    {
        _scan = Bind("ScanStart", true, "A bioscan starting (the loudspeaker announces it) makes the noise around the scan.");
        _open = Bind("DoorOpen", false, "The first security door (out of the elevator zone) makes the noise when it opens.");
        _all = Bind("AllDoors", false, "DoorOpen: every security door, not only the first one.");
        _elevator = Bind("ElevatorLanding", false, "The elevator landing makes the noise around the cage.");
        _delay = Bind("Delay", 0.5f, "Seconds after the event before the noise (the loudspeaker).");
        _wake = BindMigrated(Section, "WakeRadius", 15f, "Sleepers within this distance (m) wake up (through walls). 0 — none.", (Section, "WakeRadius", "20"), (Section, "WakeRadius", "25"));
        _stir = BindMigrated(Section, "StirRadius", 35f, "Sleepers up to this distance (m) stir. 0 — none.", (Section, "StirRadius", "40"), (Section, "StirRadius", "50"));
        _stirWakes = Bind("StirWakes", false, "Stirred enemies get regular noise (may wake) instead of a pulse.");
    }

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _pending.Clear(); _done.Clear(); _inLevelAt = -1f; };
    }

    private static readonly List<(Vector3 Pos, float At)> _pending = new();
    private static readonly HashSet<IntPtr> _done = new();
    private static float _inLevelAt = -1f;

    [HarmonyPatch(typeof(LG_SecurityDoor), nameof(LG_SecurityDoor.OnDoorIsOpened))]
    private static class Patch_Opened
    {
        private static void Postfix(LG_SecurityDoor __instance)
        {
            try
            {
                if (__instance == null || !SNet.IsMaster || !GameUtil.InLevel) return;
                if (_inLevelAt < 0f || Time.time - _inLevelAt < 3f) return; // doors opened by the level itself at the start
                if (!_done.Add(__instance.Pointer)) return;
                bool first = IsFirst(__instance);
                var z = __instance.Gate?.ProgressionSourceArea?.m_zone;
                ModInfo.Log.LogInfo($"[DoorWake] security door opened, from {(z == null ? "?" : $"{z.m_layer?.m_type} {z.LocalIndex}")}{(first ? " — the first door" : "")}");
                if (!_open.Value || (!_all.Value && !first)) return;
                _pending.Add((__instance.transform.position, Time.time + Math.Max(0f, _delay.Value)));
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[DoorWake] {e.Message}"); }
        }
    }

    /// <summary>A bioscan starts (the host activates it): the noise around it.</summary>
    [HarmonyPatch(typeof(ChainedPuzzleInstance), nameof(ChainedPuzzleInstance.MasterActivate))]
    private static class Patch_Scan
    {
        private static void Postfix(ChainedPuzzleInstance __instance)
        {
            try
            {
                if (!_scan.Value || __instance == null || !SNet.IsMaster || !GameUtil.InLevel) return;
                Vector3? pos = null;
                try { var cores = __instance.m_chainedPuzzleCores; if (cores != null && cores.Length > 0) pos = ((Il2CppObjectBase)cores[0]).TryCast<Component>()?.transform.position; } catch { }
                pos ??= __instance.m_parent != null ? __instance.m_parent.position : GameUtil.LocalAgent?.Position;
                if (pos == null) return;
                ModInfo.Log.LogInfo($"[DoorWake] bioscan started at {pos.Value}");
                _pending.Add((pos.Value, Time.time + Math.Max(0f, _delay.Value)));
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[DoorWake] scan: {e.Message}"); }
        }
    }

    /// <summary>The door out of the elevator zone: its gate's source area is in the main layer's zone 0.</summary>
    private static bool IsFirst(LG_SecurityDoor door)
    {
        var area = door.Gate?.ProgressionSourceArea;
        var zone = area?.m_zone;
        return zone != null && zone.LocalIndex == GameData.eLocalZoneIndex.Zone_0 && zone.m_layer != null && zone.m_layer.m_type == LG_LayerType.MainLayer;
    }

    private static void Tick()
    {
        if (!GameUtil.InLevel) { _inLevelAt = -1f; return; }
        if (_inLevelAt < 0f)
        {
            _inLevelAt = Time.time;
            // The elevator has landed (InLevel starts with the cage): the noise around it, if asked for.
            if (_elevator.Value && SNet.IsMaster && GameUtil.LocalAgent != null) _pending.Add((GameUtil.LocalAgent.Position, Time.time + 3f));
        }
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (Time.time < _pending[i].At) continue;
            var pos = _pending[i].Pos;
            _pending.RemoveAt(i);
            try { Wake(pos); } catch (Exception e) { ModInfo.Log.LogWarning($"[DoorWake] {e.Message}"); }
        }
    }

    private static void Wake(Vector3 from)
    {
        var me = GameUtil.LocalAgent;
        if (me == null) return;
        float wake = Math.Max(0f, _wake.Value), stir = Math.Max(_stir.Value, wake);
        if (stir <= 0f) return;
        var maker = ((Il2CppObjectBase)me).Cast<INM_NoiseMaker>();
        var seen = new HashSet<IntPtr>();
        int woke = 0, stirred = 0;
        foreach (var col in Physics.OverlapSphere(from, stir, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var limb = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>();
            var enemy = limb?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var node = enemy.CourseNode;
            if (node == null) continue;
            // Behind another closed security door it isn't heard.
            if (node.m_zone.LocalIndex != GameData.eLocalZoneIndex.Zone_0 && node.m_zone.m_sourceGate != null && !node.m_zone.m_sourceGate.HasBeenOpenedDuringPlay) continue;
            bool w = Vector3.Distance(from, enemy.Position) <= wake;
            if (w) woke++; else stirred++;
            NoiseManager.MakeNoise(new NM_NoiseData
            {
                noiseMaker = maker,
                position = enemy.Position,
                node = node,
                radiusMin = 0.01f,
                radiusMax = 1f,
                yScale = 1f,
                type = w ? NM_NoiseType.InstaDetect : (_stirWakes.Value ? NM_NoiseType.Detectable : NM_NoiseType.PulseOnly),
                includeToNeightbourAreas = false,
                raycastFirstNode = false,
            });
        }
        ModInfo.Log.LogInfo($"[DoorWake] noise at {from}: woke {woke}, stirred {stirred}");
    }
}
