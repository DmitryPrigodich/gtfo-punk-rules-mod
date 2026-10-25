using AIGraph;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Infection that doesn't sit still:
///  - once infected, it keeps growing slowly (1% every 2 minutes by default) until disinfected;
///  - from 70% the player coughs now and then — a noise sleepers nearby notice (more often the worse it gets);
///  - from 50%, running leaves you gasping for breath — a quieter noise that unsettles sleepers;
///  - from 70% the view trembles slightly (stronger towards 100%);
///  - optionally (off by default) disinfection removes only part of the infection and pauses the growth for a while.
/// The host runs it for every player (bots too) and tells the others when someone coughs, so everyone hears it.
/// Everyone needs it.
/// </summary>
public class Infection : ModuleBase
{
    public override string Id => "Infection";
    public override string Description => "Infection keeps growing until disinfected; badly infected players cough, disturbing sleepers nearby. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<float> _growPercent, _growMinutes, _growCap, _coughFrom, _coughMin, _coughMax, _coughRadius;
    private static ConfigEntry<float> _breathFrom, _breathAfter, _breathRadius, _shakeFrom, _shakeMax, _disinfect, _pauseMinutes;

    protected override void OnBindConfig()
    {
        _growPercent = Bind("GrowthPercent", 1f, "While infected, infection grows by this many percent…");
        _growMinutes = Bind("GrowthMinutes", 2f, "…every this many minutes. 0 — no growth.");
        _growCap = Bind("GrowthCap", 95f, "Growth stops at this infection (%). Disinfection works as usual.");
        _coughFrom = Bind("CoughFrom", 70f, "Players cough from this infection (%). 0 — never.");
        _coughMin = Bind("CoughIntervalMin", 25f, "Shortest time between coughs at the threshold (s); at full infection it's half as long.");
        _coughMax = Bind("CoughIntervalMax", 50f, "Longest time between coughs at the threshold (s).");
        _coughRadius = Bind("CoughNoiseRadius", 10f, "Sleepers within this radius (m) notice a cough; a few coughs in a row can wake them.");
        _breathFrom = Bind("BreathFrom", 50f, "From this infection (%), running leaves you gasping. 0 — never.");
        _breathAfter = Bind("BreathAfterRun", 4f, "Seconds of running before you gasp (when you stop, or every twice as long while still running).");
        _breathRadius = Bind("BreathNoiseRadius", 6f, "Sleepers within this radius (m) are unsettled by the gasping (it doesn't wake them by itself).");
        _shakeFrom = Bind("TremorFrom", 70f, "From this infection (%), your view trembles slightly. 0 — never.");
        _shakeMax = Bind("TremorAmount", 0.4f, "Trembling at 100% infection (camera shake; grows from 0 at TremorFrom).");
        _disinfect = Bind("DisinfectionStrength", 1f, "Part of the infection a disinfection removes (1 — as in the game, 0.5 — half).");
        _pauseMinutes = Bind("DisinfectionPauseMinutes", 0f, "After a disinfection the growth pauses for this many minutes (0 — no pause).");
    }

    protected override void OnEnable()
    {
        Net.On(Net.Cough, OnCoughReceived);
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _grown.Clear(); _nextCough.Clear(); _runTime.Clear(); _lastPos.Clear(); _pausedUntil.Clear(); StopTremor(); };
    }

    private static readonly Dictionary<int, float> _grown = new(), _nextCough = new(), _runTime = new(), _pausedUntil = new();
    private static readonly Dictionary<int, Vector3> _lastPos = new();
    private static float _nextTick;
    private static readonly System.Random _rng = new();

    private static void Tick()
    {
        TremorTick();
        if (!GameUtil.InLevel || !SNet.IsMaster || Time.time < _nextTick) return;
        const float step = 1f;
        _nextTick = Time.time + step;
        foreach (var p in PlayerManager.PlayerAgentsInLevel)
        {
            if (p == null || !p.Alive || p.Damage == null) continue;
            int slot = p.PlayerSlotIndex;
            float inf = p.Damage.Infection;
            if (!(_pausedUntil.TryGetValue(slot, out var until) && Time.time < until)) Grow(p, slot, inf, step);
            CoughCheck(p, slot, inf);
            BreathCheck(p, slot, inf, step);
        }
    }

    /// <summary>Accumulates growth and applies it a whole percent at a time (fewer network updates).</summary>
    private static void Grow(PlayerAgent p, int slot, float inf, float dt)
    {
        if (inf <= 0.001f || _growMinutes.Value <= 0f || inf * 100f >= _growCap.Value) { _grown.Remove(slot); return; }
        _grown.TryGetValue(slot, out float acc);
        acc += _growPercent.Value / 100f * dt / (_growMinutes.Value * 60f);
        if (acc >= 0.01f)
        {
            float add = Math.Min(acc, _growCap.Value / 100f - inf);
            p.Damage.ModifyInfection(new pInfection { amount = add, mode = pInfectionMode.Add, effect = pInfectionEffect.None }, true, true);
            ModInfo.Log.LogInfo($"[Infection] {p.PlayerName}: {inf * 100f:0}% → {(inf + add) * 100f:0}%");
            acc = 0f;
        }
        _grown[slot] = acc;
    }

    private static void CoughCheck(PlayerAgent p, int slot, float inf)
    {
        float from = _coughFrom.Value / 100f;
        if (from <= 0f || inf < from) { _nextCough.Remove(slot); return; }
        if (!_nextCough.TryGetValue(slot, out float at)) { _nextCough[slot] = Time.time + NextInterval(inf, from); return; }
        if (Time.time < at) return;
        _nextCough[slot] = Time.time + NextInterval(inf, from);
        bool hard = inf >= (from + 1f) * 0.5f;
        PlaySound(p, hard ? Sound.Hard : Sound.Soft);
        MakeNoise(p, _coughRadius.Value);
        Net.SendToAll(Net.Cough, new[] { (byte)slot, (byte)(hard ? Sound.Hard : Sound.Soft) });
        ModInfo.Log.LogInfo($"[Infection] {p.PlayerName} coughs ({inf * 100f:0}%, {(hard ? "hard" : "soft")})");
    }

    private static float NextInterval(float inf, float from)
    {
        float lo = Math.Max(3f, _coughMin.Value), hi = Math.Max(lo, _coughMax.Value);
        float k = Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(from, 1f, inf)); // worse — more often
        return (float)(lo + (hi - lo) * _rng.NextDouble()) * k;
    }

    private enum Sound : byte { Soft = 0, Hard = 1, Gasp = 2 }

    private static void PlaySound(PlayerAgent p, Sound s)
    {
        uint id = s switch { Sound.Hard => AK.EVENTS.PLAY_COUGHHARD01, Sound.Gasp => AK.EVENTS.INTENSITY_3_GASPING, _ => AK.EVENTS.PLAY_COUGHSOFT01 };
        try { p.Sound.Post(id, p.Position); }
        catch { try { CellSound.Post(id, p.Position); } catch { } }
    }

    /// <summary>Running while badly infected: after BreathAfterRun seconds of running — a gasp when you stop (or every twice that while running on).</summary>
    private static void BreathCheck(PlayerAgent p, int slot, float inf, float dt)
    {
        var pos = p.Position;
        _lastPos.TryGetValue(slot, out var last);
        _lastPos[slot] = pos;
        float from = _breathFrom.Value / 100f;
        if (from <= 0f || inf < from || last == Vector3.zero) { _runTime.Remove(slot); return; }
        var d = pos - last; d.y = 0f;
        bool running = d.magnitude / dt > 4f; // walking is ~2.5 m/s, running ~6
        _runTime.TryGetValue(slot, out float run);
        float after = Math.Max(1f, _breathAfter.Value);
        bool gasp = false;
        if (running) { run += dt; if (run >= after * 2f) { gasp = true; run = after; } }
        else { if (run >= after) gasp = true; run = 0f; }
        _runTime[slot] = run;
        if (!gasp) return;
        PlaySound(p, Sound.Gasp);
        MakeNoise(p, _breathRadius.Value, NM_NoiseType.PulseOnly);
        Net.SendToAll(Net.Cough, new[] { (byte)slot, (byte)Sound.Gasp });
        ModInfo.Log.LogInfo($"[Infection] {p.PlayerName} gasps for breath ({inf * 100f:0}%)");
    }

    // ---------- tremor (local player, every machine) ----------

    private static bool _tremorSet;
    private static float _nextTremor;

    private static void TremorTick()
    {
        if (Time.time < _nextTremor) return;
        _nextTremor = Time.time + 0.5f;
        var p = GameUtil.LocalAgent;
        float from = _shakeFrom.Value / 100f;
        float inf = 0f;
        try { inf = p != null && p.Alive && GameUtil.InLevel ? p.Damage.Infection : 0f; } catch { }
        if (from <= 0f || inf < from || _shakeMax.Value <= 0f) { StopTremor(); return; }
        float k = Mathf.InverseLerp(from, 1f, inf);
        try { p.FPSCamera?.SetConstantCameraShakeAmount(_shakeMax.Value * Mathf.Max(0.15f, k)); _tremorSet = true; } catch { }
    }

    private static void StopTremor()
    {
        if (!_tremorSet) return;
        _tremorSet = false;
        try { GameUtil.LocalAgent?.FPSCamera?.SetConstantCameraShakeAmount(0f); } catch { }
    }

    // ---------- disinfection (optional: weaker, pauses the growth) ----------

    [HarmonyPatch(typeof(Dam_PlayerDamageBase), nameof(Dam_PlayerDamageBase.ModifyInfection))]
    private static class Patch_Disinfect
    {
        private static void Prefix(Dam_PlayerDamageBase __instance, ref pInfection data, bool sync)
        {
            try
            {
                if (data.effect == pInfectionEffect.None || data.amount >= 0f || !sync) return;
                float k = Mathf.Clamp01(_disinfect.Value);
                if (k < 0.999f) data.amount *= k;
                if (_pauseMinutes.Value > 0f && __instance.Owner != null) _pausedUntil[__instance.Owner.PlayerSlotIndex] = Time.time + _pauseMinutes.Value * 60f;
                ModInfo.Log.LogInfo($"[Infection] disinfection ({data.effect}): {data.amount * 100f:0}%{(_pauseMinutes.Value > 0f ? $", growth paused for {_pauseMinutes.Value:0.#} min" : "")}");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Infection] disinfection: {e.Message}"); }
        }
    }

    private static void MakeNoise(PlayerAgent p, float radius, NM_NoiseType type = NM_NoiseType.Detectable)
    {
        if (radius <= 0f || p.CourseNode == null) return;
        NoiseManager.MakeNoise(new NM_NoiseData
        {
            noiseMaker = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)p).Cast<INM_NoiseMaker>(),
            position = p.Position,
            node = p.CourseNode,
            radiusMin = radius * 0.5f,
            radiusMax = radius,
            yScale = 1f,
            type = type,
            includeToNeightbourAreas = true,
            raycastFirstNode = false,
        });
    }

    /// <summary>Other machines: the host says someone coughed / gasped — play it there.</summary>
    private static void OnCoughReceived(ulong sender, byte[] data)
    {
        if (data == null || data.Length < 2 || SNet.IsMaster) return;
        int slot = data[0];
        if (PlayerManager.TryGetPlayerAgent(ref slot, out var p) && p != null) PlaySound(p, (Sound)data[1]);
    }
}
