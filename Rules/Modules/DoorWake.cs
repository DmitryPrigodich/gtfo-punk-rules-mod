using Il2CppInterop.Runtime.InteropTypes;
using Enemies;
using LevelGeneration;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The first security door is loud: the one leading out of the elevator zone wakes the sleepers around it when it opens
/// (within WakeRadius, through walls) and stirs those further off (up to StirRadius) — the room behind it has to be
/// cleared. Zones behind other closed doors don't hear it. The host makes the noise. AllDoors — every security door.
/// </summary>
public class DoorWake : ModuleBase
{
    public override string Id => "DoorWake";
    public override string Section => "Level.FirstDoor";
    public override IReadOnlyList<string> OldSections => new[] { "Level.Elevator" };
    public override string Description => "The first security door (out of the elevator zone) is loud: opening it wakes the sleepers around it. Host only.";
    public override ModuleSide Side => ModuleSide.Host;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<float> _delay, _wake, _stir;
    private static ConfigEntry<bool> _stirWakes, _all;

    protected override void OnBindConfig()
    {
        _delay = Bind("Delay", 0.5f, "Seconds after the door has opened before the noise.");
        _wake = BindMigrated(Section, "WakeRadius", 20f, "Sleepers within this distance (m) of the door wake up (through walls). 0 — none.", (Section, "WakeRadius", "25"));
        _stir = BindMigrated(Section, "StirRadius", 40f, "Sleepers up to this distance (m) stir. 0 — none.", (Section, "StirRadius", "50"));
        _stirWakes = Bind("StirWakes", false, "Stirred enemies get regular noise (may wake) instead of a pulse.");
        _all = Bind("AllDoors", false, "Every security door is loud, not only the first one.");
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
                if (!_all.Value && !first) return;
                _pending.Add((__instance.transform.position, Time.time + Math.Max(0f, _delay.Value)));
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[DoorWake] {e.Message}"); }
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
        if (_inLevelAt < 0f) _inLevelAt = Time.time;
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
        ModInfo.Log.LogInfo($"[DoorWake] the door at {from} woke {woke} and stirred {stirred}");
    }
}
