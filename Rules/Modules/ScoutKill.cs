using Enemies;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Scouts can be stopped: from the moment a scout catches you (its feelers) until the scream goes off it takes more
/// damage (ChargeupDamage); the charge-up can be made longer (ScreamChargeup) — kill it in that window and there's no
/// scream and no wave. Once the scream goes off — as in the game. Everyone needs it (the host runs the enemies).
/// </summary>
public class ScoutKill : ModuleBase
{
    public override string Id => "ScoutKill";
    public override string Description => "Kill a scout in the first seconds of its scream (longer charge-up, extra damage meanwhile) and the wave doesn't come. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<float> _chargeup, _damage;

    protected override void OnBindConfig()
    {
        _chargeup = Bind("ScreamChargeup", 2f, "Seconds a scout charges its scream before it goes off (at least the game's own). 0 — as in the game.");
        _damage = Bind("ChargeupDamage", 3f, "Damage multiplier on a scout from the moment it catches you until the scream goes off. 1 — normal.");
    }

    protected override void OnEnable() => FrameTicker.OnUpdate += Tick;

    private static bool _applied;

    private static void Tick()
    {
        if (_applied || !GameUtil.InLevel) return;
        _applied = true;
        try
        {
            float was = ES_ScoutScream.ChargeupTime;
            if (_chargeup.Value > was) ES_ScoutScream.ChargeupTime = _chargeup.Value;
            ModInfo.Log.LogInfo($"[ScoutKill] scream charge-up {was:0.##} s → {ES_ScoutScream.ChargeupTime:0.##} s (scream {ES_ScoutScream.ScreamTime:0.##} s, response after {ES_ScoutScream.TimeToResponse:0.##} s)");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[ScoutKill] charge-up: {e.Message}"); }
    }

    private static int _logs;
    private static readonly HashSet<string> _outLogs = new();

    /// <summary>Host side: all damage to an enemy ends up here.</summary>
    [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.ProcessReceivedDamage))]
    private static class Patch_Damage
    {
        private static void Prefix(Dam_EnemyDamageBase __instance, ref float damage)
        {
            if (_damage.Value <= 1.001f) return;
            try
            {
                var loco = __instance.Owner?.Locomotion;
                if (loco == null) return;
                // From the moment it catches you (its feelers) until the scream goes off.
                var st = loco.CurrentStateEnum;
                bool window = st == ES_StateEnum.ScoutDetection;
                if (st == ES_StateEnum.ScoutScream)
                {
                    var scream = loco.ScoutScream;
                    window = scream != null && (scream.m_state == ES_ScoutScream.ScoutScreamState.Setup || scream.m_state == ES_ScoutScream.ScoutScreamState.Chargeup);
                }
                if (!window)
                {
                    // Diagnostics: other scout kinds (e.g. the crawling ones) — which state are they in when hit?
                    var owner = __instance.Owner;
                    if (owner != null && owner.IsScout && _outLogs.Add(owner.EnemyDataID + ":" + st))
                        ModInfo.Log.LogInfo($"[ScoutKill] scout '{owner.EnemyData?.name}' ({owner.EnemyDataID}) hit outside the window: state {st}"
                            + (loco.ScoutScream != null ? $", scream {loco.ScoutScream.m_state}" : ""));
                    return;
                }
                damage *= _damage.Value;
                if (_logs++ < 5) ModInfo.Log.LogInfo($"[ScoutKill] hit in the window ({st}): ×{_damage.Value:0.#} → {damage:0.#}");
            }
            catch { }
        }
    }
}
