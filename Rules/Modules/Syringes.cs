using System;
using System.Collections.Generic;
using System.Linq;
using Agents;
using GameData;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Booster syringes instead of artifacts.
///  - Every artifact spot in the level gets a "booster syringe" (a copy of the IIx syringe: same model and injection animation),
///    not registered in the terminal — you find it by looking around, not by pinging.
///  - Injecting it applies the effects of a random booster template from the game data (the same effects boosters give),
///    for SyringeMinutes (5 by default). Effects are synced through the game's own modifier sync.
///  - Active effects and time left are shown on the HUD above the watermark.
/// Level generation changes, so everyone needs the mod.
/// </summary>
public class Syringes : ModuleBase
{
    public override string Id => "Syringes";
    public override string Description => "Booster syringes lie where artifacts used to be; injecting one gives random booster effects for 5 minutes. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    public const uint SyringeItemId = 4251;
    private const uint BaseSyringeId = 142; // IIx Syringe (melee buff)

    private static BepInEx.Configuration.ConfigEntry<bool> _replaceArtifacts;
    private static BepInEx.Configuration.ConfigEntry<float> _minutes;
    private static BepInEx.Configuration.ConfigEntry<bool> _downsides;

    protected override void OnBindConfig()
    {
        _replaceArtifacts = Bind("ReplaceArtifacts", true, "Put a booster syringe on every artifact spot instead of the artifact.");
        _minutes = Bind("SyringeMinutes", 5f, "How long the effects of one syringe last (minutes).");
        _downsides = Bind("KeepDownsides", true, "Keep the negative effects some boosters have (as in the game). false — only the positive ones.");
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { ClearAll(); _hud = null; };
    }

    // ---------- item ----------

    private static void Inject()
    {
        var src = GameDataBlockBase<ItemDataBlock>.GetBlock(BaseSyringeId);
        if (src == null) { ModInfo.Log.LogWarning("[Syringes] IIx syringe (142) not in game data — no booster syringe"); return; }
        if (GameDataBlockBase<ItemDataBlock>.HasBlock(SyringeItemId)) return;
        var item = GearFactory.CloneBlock(src);
        item.persistentID = SyringeItemId;
        item.name = "GtfoPunk_BoosterSyringe";
        item.publicName = Lang.T("Booster Syringe", "Шприц-усилитель");
        try { item.LocalizedName = GearFactory.Text(item.publicName); } catch { }
        item.registerInTerminalSystem = false; // not findable through terminal ping
        GameDataBlockBase<ItemDataBlock>.AddBlock(item);
        int templates = Templates().Count;
        ModInfo.Log.LogInfo($"[Syringes] booster syringe item {SyringeItemId} added; booster templates available: {templates}");
    }

    // ---------- artifacts → syringes (level generation, same on every machine) ----------

    [HarmonyPatch(typeof(LG_PickupItem), nameof(LG_PickupItem.SetupAsArtifact))]
    private static class Patch_Artifact
    {
        private static bool Prefix(LG_PickupItem __instance, int randomSeed)
        {
            if (!_replaceArtifacts.Value || !GameDataBlockBase<ItemDataBlock>.HasBlock(SyringeItemId)) return true;
            try
            {
                __instance.SetupAsConsumable(randomSeed, SyringeItemId);
                _replaced++;
                return false;
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Syringes] artifact → syringe: {e.Message}"); return true; }
        }
    }

    private static int _replaced;

    // ---------- injection ----------

    private static float _suppressMeleeUntil;

    // ApplySyringe() is inlined into Update() by IL2CPP, so a patch on it never fires.
    // Its coroutine body (MoveNext of the generated iterator) is a real virtual call — hook its first step.
    [HarmonyPatch(typeof(SyringeFirstPerson._ApplySyringe_d__18), nameof(SyringeFirstPerson._ApplySyringe_d__18.MoveNext))]
    private static class Patch_Apply
    {
        private static void Prefix(SyringeFirstPerson._ApplySyringe_d__18 __instance)
        {
            try
            {
                if (__instance == null || __instance.__1__state != 0) return; // only the first step of each injection
                var syringe = __instance.__4__this;
                uint id = syringe?.ItemDataBlock?.persistentID ?? 0;
                ModInfo.Log.LogInfo($"[Syringes] syringe used: item {id}, type {syringe?.m_type}");
                if (id != SyringeItemId) return;
                // The copied IIx would also give its melee buff — block it for the next few seconds and roll our own effects.
                _suppressMeleeUntil = Time.time + 6f;
                _pendingApplyAt = Time.time + 1.2f; // roughly when the needle goes in
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Syringes] {e.Message}"); }
        }
    }

    private static bool BlockMelee(Agent agent, AgentModifier modifier)
    {
        if (modifier != AgentModifier.MeleeDamage || Time.time > _suppressMeleeUntil) return false;
        var local = PlayerManager.GetLocalPlayerAgent();
        return local != null && agent != null && agent.Pointer == local.Pointer;
    }

    [HarmonyPatch(typeof(AgentModifierManager), nameof(AgentModifierManager.AddSyncedModifierValue))]
    private static class Patch_SyncedAdd
    {
        private static bool Prefix(Agent agent, AgentModifier modifier, ref uint __result) { if (!BlockMelee(agent, modifier)) return true; __result = 0; return false; }
    }

    [HarmonyPatch(typeof(AgentModifierManager), nameof(AgentModifierManager.AddModifierValue), new[] { typeof(Agent), typeof(AgentModifier), typeof(float), typeof(float) })]
    private static class Patch_Add
    {
        private static bool Prefix(Agent agent, AgentModifier modifier, ref uint __result) { if (!BlockMelee(agent, modifier)) return true; __result = 0; return false; }
    }

    // ---------- effects ----------

    private class Boost
    {
        public string Name;
        public float Until;
        public readonly List<uint> Ids = new();
        public readonly List<string> Lines = new();
    }

    private static readonly List<Boost> _active = new();
    private static float _pendingApplyAt = -1f;

    private static List<BoosterImplantTemplateDataBlock> Templates() =>
        GameDataBlockBase<BoosterImplantTemplateDataBlock>.GetAllBlocks().ToArray()
            .Where(t => t != null && !t.Deprecated && ((t.Effects?.Count ?? 0) > 0 || (t.RandomEffects?.Count ?? 0) > 0)).ToList();

    private static readonly System.Random _rng = new();

    private static void ApplyRandomBooster()
    {
        var agent = PlayerManager.GetLocalPlayerAgent();
        if (agent == null) return;
        var templates = Templates();
        if (templates.Count == 0) { ModInfo.Log.LogWarning("[Syringes] no booster templates in game data"); return; }
        var tpl = templates[_rng.Next(templates.Count)];

        var effects = new List<BoosterImplantEffectInstance>();
        if (tpl.Effects != null) foreach (var e in tpl.Effects) if (e != null) effects.Add(e);
        if (tpl.RandomEffects != null)
            foreach (var group in tpl.RandomEffects)
                if (group != null && group.Count > 0) effects.Add(group[_rng.Next(group.Count)]);

        var boost = new Boost { Name = (string)tpl.PublicName ?? tpl.name, Until = Time.time + Math.Max(0.1f, _minutes.Value) * 60f };
        foreach (var inst in effects)
        {
            var eff = GameDataBlockBase<BoosterImplantEffectDataBlock>.GetBlock(inst.BoosterImplantEffect);
            if (eff == null) continue;
            float value = Mathf.Lerp(inst.MinValue, inst.MaxValue, (float)_rng.NextDouble());
            // Booster values are multipliers around 1 (1.15 = +15%); modifiers take the change.
            float delta = value > 0.2f && value < 5f ? value - 1f : value;
            bool good = AgentModifierManager.ModifierHasInvertedMeaning(eff.Effect) ? delta < 0f : delta > 0f;
            if (!good && !_downsides.Value) continue;
            uint id = AgentModifierManager.AddSyncedModifierValue(agent, eff.Effect, delta);
            boost.Ids.Add(id);
            string name = (string)eff.PublicShortName;
            if (string.IsNullOrWhiteSpace(name)) name = eff.Effect.ToString();
            boost.Lines.Add($"<color={(good ? "#8fe08f" : "#e08f8f")}>{name} {delta * 100f:+0;-0}%</color>");
            ModInfo.Log.LogInfo($"[Syringes] {eff.Effect}: value {value:0.###} → change {delta:+0.###;-0.###} (id {id})");
        }
        if (boost.Ids.Count == 0) return;
        _active.Add(boost);
        ModInfo.Log.LogInfo($"[Syringes] injected '{boost.Name}' for {_minutes.Value:0.#} min");
        try { PlayerChatManager.PostChatMessageLocaly(SNet.LocalPlayer, $"<color=#ffd34f>{Lang.T("Booster syringe", "Шприц-усилитель")}:</color> {string.Join(", ", boost.Lines)}"); } catch { }
    }

    private static void Expire(Boost b)
    {
        foreach (var id in b.Ids) try { AgentModifierManager.ClearSyncedModifierChange(id); } catch { }
        ModInfo.Log.LogInfo($"[Syringes] '{b.Name}' wore off");
    }

    private static void ClearAll()
    {
        foreach (var b in _active) Expire(b);
        _active.Clear();
        _pendingApplyAt = -1f;
        if (_replaced > 0) ModInfo.Log.LogInfo($"[Syringes] artifacts replaced by syringes this level: {_replaced}");
        _replaced = 0;
    }

    // ---------- tick + HUD ----------

    private static TMPro.TextMeshPro _hud;

    private static void Tick()
    {
        if (_pendingApplyAt > 0f && Time.time >= _pendingApplyAt)
        {
            _pendingApplyAt = -1f;
            try { ApplyRandomBooster(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Syringes] apply: {e.Message}"); }
        }
        for (int i = _active.Count - 1; i >= 0; i--)
            if (Time.time >= _active[i].Until) { Expire(_active[i]); _active.RemoveAt(i); }
        UpdateHud();
    }

    /// <summary>A copy of the watermark text, a bit higher, listing the active syringe effects and time left.</summary>
    private static void UpdateHud()
    {
        if (_hud == null)
        {
            if (_active.Count == 0) return;
            var wm = UnityEngine.Object.FindObjectOfType<PUI_Watermark>();
            var src = wm?.m_watermarkText;
            if (src == null) return;
            var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent, false);
            go.name = "GtfoPunk_SyringeHud";
            go.transform.localPosition = src.transform.localPosition + new Vector3(0f, 60f, 0f);
            _hud = go.GetComponent<TMPro.TextMeshPro>();
            if (_hud == null) { UnityEngine.Object.Destroy(go); return; }
        }
        string text = "";
        if (_active.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in _active)
            {
                int left = Mathf.CeilToInt(b.Until - Time.time);
                sb.Append($"<color=#ffd34f>{left / 60}:{left % 60:00}</color> ").Append(string.Join("  ", b.Lines)).Append('\n');
            }
            text = sb.ToString().TrimEnd('\n');
        }
        if (_hud.text != text) _hud.SetText(text);
    }
}
