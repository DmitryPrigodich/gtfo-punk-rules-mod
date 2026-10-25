using AIGraph;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Infection that doesn't sit still:
///  - once infected, it keeps growing slowly (1% every 2 minutes by default) until disinfected;
///  - from 70% the player coughs now and then — a noise sleepers nearby notice (more often the worse it gets).
/// The host runs it for every player (bots too) and tells the others when someone coughs, so everyone hears it.
/// Everyone needs it.
/// </summary>
public class Infection : ModuleBase
{
    public override string Id => "Infection";
    public override string Description => "Infection keeps growing until disinfected; badly infected players cough, disturbing sleepers nearby. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<float> _growPercent, _growMinutes, _growCap, _coughFrom, _coughMin, _coughMax, _coughRadius;

    protected override void OnBindConfig()
    {
        _growPercent = Bind("GrowthPercent", 1f, "While infected, infection grows by this many percent…");
        _growMinutes = Bind("GrowthMinutes", 2f, "…every this many minutes. 0 — no growth.");
        _growCap = Bind("GrowthCap", 95f, "Growth stops at this infection (%). Disinfection works as usual.");
        _coughFrom = Bind("CoughFrom", 70f, "Players cough from this infection (%). 0 — never.");
        _coughMin = Bind("CoughIntervalMin", 25f, "Shortest time between coughs at the threshold (s); at full infection it's half as long.");
        _coughMax = Bind("CoughIntervalMax", 50f, "Longest time between coughs at the threshold (s).");
        _coughRadius = Bind("CoughNoiseRadius", 10f, "Sleepers within this radius (m) notice a cough; a few coughs in a row can wake them.");
    }

    protected override void OnEnable()
    {
        Net.On(Net.Cough, OnCoughReceived);
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _grown.Clear(); _nextCough.Clear(); };
    }

    private static readonly Dictionary<int, float> _grown = new(), _nextCough = new();
    private static float _nextTick;
    private static readonly System.Random _rng = new();

    private static void Tick()
    {
        if (!GameUtil.InLevel || !SNet.IsMaster || Time.time < _nextTick) return;
        const float step = 1f;
        _nextTick = Time.time + step;
        foreach (var p in PlayerManager.PlayerAgentsInLevel)
        {
            if (p == null || !p.Alive || p.Damage == null) continue;
            int slot = p.PlayerSlotIndex;
            float inf = p.Damage.Infection;
            Grow(p, slot, inf, step);
            CoughCheck(p, slot, inf);
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
        PlayCough(p, hard);
        MakeNoise(p);
        Net.SendToAll(Net.Cough, new[] { (byte)slot, (byte)(hard ? 1 : 0) });
        ModInfo.Log.LogInfo($"[Infection] {p.PlayerName} coughs ({inf * 100f:0}%, {(hard ? "hard" : "soft")})");
    }

    private static float NextInterval(float inf, float from)
    {
        float lo = Math.Max(3f, _coughMin.Value), hi = Math.Max(lo, _coughMax.Value);
        float k = Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(from, 1f, inf)); // worse — more often
        return (float)(lo + (hi - lo) * _rng.NextDouble()) * k;
    }

    private static void PlayCough(PlayerAgent p, bool hard)
    {
        try { p.Sound.Post(hard ? AK.EVENTS.PLAY_COUGHHARD01 : AK.EVENTS.PLAY_COUGHSOFT01, p.Position); }
        catch { try { CellSound.Post(hard ? AK.EVENTS.PLAY_COUGHHARD01 : AK.EVENTS.PLAY_COUGHSOFT01, p.Position); } catch { } }
    }

    private static void MakeNoise(PlayerAgent p)
    {
        if (_coughRadius.Value <= 0f || p.CourseNode == null) return;
        NoiseManager.MakeNoise(new NM_NoiseData
        {
            noiseMaker = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)p).Cast<INM_NoiseMaker>(),
            position = p.Position,
            node = p.CourseNode,
            radiusMin = _coughRadius.Value * 0.5f,
            radiusMax = _coughRadius.Value,
            yScale = 1f,
            type = NM_NoiseType.Detectable,
            includeToNeightbourAreas = true,
            raycastFirstNode = false,
        });
    }

    /// <summary>Other machines: the host says someone coughed — play it there.</summary>
    private static void OnCoughReceived(ulong sender, byte[] data)
    {
        if (data == null || data.Length < 2 || SNet.IsMaster) return;
        int slot = data[0];
        if (PlayerManager.TryGetPlayerAgent(ref slot, out var p) && p != null) PlayCough(p, data[1] == 1);
    }
}
