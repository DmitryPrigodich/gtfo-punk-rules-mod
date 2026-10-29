using Il2CppInterop.Runtime.InteropTypes;
using Enemies;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The drop is loud: when the cage lands and the loudspeaker announces the expedition, the sleepers around the elevator
/// wake up (within WakeRadius, through walls) and the ones further off stir (up to StirRadius) — the first room has to be
/// cleared. Only the elevator's own zone and zones whose doors are open hear it. The host makes the noise.
/// </summary>
public class ElevatorWake : ModuleBase
{
    public override string Id => "ElevatorWake";
    public override string Section => "Level.Elevator";
    public override string Description => "The drop is loud: when the elevator lands, sleepers around it wake up (the first room has to be cleared). Host only.";
    public override ModuleSide Side => ModuleSide.Host;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static ConfigEntry<float> _delay, _wake, _stir;
    private static ConfigEntry<bool> _stirWakes;

    protected override void OnBindConfig()
    {
        _delay = Bind("Delay", 3f, "Seconds after the landing (the loudspeaker) before the noise.");
        _wake = Bind("WakeRadius", 25f, "Sleepers within this distance (m) of the elevator wake up. 0 — none.");
        _stir = Bind("StirRadius", 50f, "Sleepers up to this distance (m) stir. 0 — none.");
        _stirWakes = Bind("StirWakes", false, "Stirred enemies get regular noise (may wake) instead of a pulse.");
    }

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _at = -1f; _done = false; };
    }

    private static float _at = -1f;
    private static bool _done;

    private static void Tick()
    {
        if (!GameUtil.InLevel) { _at = -1f; _done = false; return; }
        if (_done) return;
        if (_at < 0f) { _at = Time.time + Math.Max(0f, _delay.Value); return; }
        if (Time.time < _at) return;
        _done = true;
        if (!SNet.IsMaster) return;
        try { Wake(); } catch (Exception e) { ModInfo.Log.LogWarning($"[ElevatorWake] {e.Message}"); }
    }

    private static void Wake()
    {
        var me = GameUtil.LocalAgent;
        if (me == null) return;
        float wake = Math.Max(0f, _wake.Value), stir = Math.Max(_stir.Value, wake);
        if (stir <= 0f) return;
        var from = me.Position;
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
            // Behind a closed security door the loudspeaker isn't heard.
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
        ModInfo.Log.LogInfo($"[ElevatorWake] the drop woke {woke} and stirred {stirred} around {from}");
    }
}
