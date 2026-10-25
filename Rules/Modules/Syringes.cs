using Agents;
using GameData;
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

    private static ConfigEntry<bool> _replaceArtifacts;
    private static ConfigEntry<float> _minutes;
    private static ConfigEntry<bool> _downsides, _onPickup, _onMap;

    protected override void OnBindConfig()
    {
        _replaceArtifacts = Bind("ReplaceArtifacts", true, "Put a booster syringe on every artifact spot instead of the artifact.");
        _minutes = Bind("SyringeMinutes", 5f, "How long the effects of one syringe last (minutes).");
        _downsides = Bind("KeepDownsides", true, "Keep the negative effects some boosters have (as in the game). false — only the positive ones.");
        _onPickup = Bind("InjectOnPickup", true, "Picking up a booster syringe injects it at once — it doesn't take the consumable slot (and doesn't push out the grenade you carry).");
        _onMap = Bind("ShowOnMap", true, "The map screen (Tab) lists your active syringe effects with time left, and your infection.");
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { ClearAll(); _hud = null; _map = null; };
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

    /// <summary>Use on pickup: the effects right away, the syringe disappears instead of going to the consumable slot.</summary>
    [HarmonyPatch(typeof(ConsumablePickup_Core), nameof(ConsumablePickup_Core.OnInteractionPickUp))]
    private static class Patch_Pickup
    {
        private static bool Prefix(ConsumablePickup_Core __instance, PlayerAgent player)
        {
            try
            {
                if (!_onPickup.Value || __instance == null || player == null || !player.IsLocallyOwned || player.Owner == null || player.Owner.IsBot) return true;
                if ((__instance.ItemDataBlock?.persistentID ?? 0) != SyringeItemId) return true;
                var sync = __instance.GetSyncComponent();
                if (sync == null) return true;
                // Out of the level (a level pickup can't be despawned like gear).
                sync.AttemptPickupInteraction(ePickupItemInteractionType.Place, player.Owner, sync.GetCustomData(), new Vector3(0f, -5000f, 0f), Quaternion.identity);
                try { CellSound.Post(AK.EVENTS.SYRINGE_USE, player.Position); } catch { }
                ModInfo.Log.LogInfo("[Syringes] syringe injected on pickup (moved out of the level)");
                ApplyRandomBooster();
                return false;
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Syringes] inject on pickup: {e.Message}"); return true; }
        }
    }

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
        public readonly List<(AgentModifier Mod, float Delta, bool Good)> Effects = new();
    }

    /// <summary>Effect name in a word or two (the game's own names are abbreviations like "Rev-Q").</summary>
    private static string EffectName(AgentModifier m) => m switch
    {
        AgentModifier.RegenerationCap => Lang.T("Regeneration cap", "Предел регенерации"),
        AgentModifier.RegenerationSpeed => Lang.T("Regeneration speed", "Скорость регенерации"),
        AgentModifier.HealSupport => Lang.T("Healing others", "Лечение других"),
        AgentModifier.ReviveSpeedSupport => Lang.T("Revive speed", "Скорость поднятия"),
        AgentModifier.ReviveStartHealthSupport => Lang.T("Revived health", "Здоровье после поднятия"),
        AgentModifier.MeleeResistance => Lang.T("Melee resistance", "Защита от ударов"),
        AgentModifier.ProjectileResistance => Lang.T("Projectile resistance", "Защита от снарядов"),
        AgentModifier.InfectionResistance => Lang.T("Infection resistance", "Защита от инфекции"),
        AgentModifier.ExplosionResistance => Lang.T("Explosion resistance", "Защита от взрывов"),
        AgentModifier.StandardWeaponDamage => Lang.T("Main weapon damage", "Урон основного оружия"),
        AgentModifier.SpecialWeaponDamage => Lang.T("Special weapon damage", "Урон спецоружия"),
        AgentModifier.MeleeDamage => Lang.T("Melee damage", "Урон в ближнем бою"),
        AgentModifier.GlueStrength => Lang.T("Foam strength", "Сила пены"),
        AgentModifier.GlueEfficiency => Lang.T("Foam efficiency", "Экономия пены"),
        AgentModifier.SentryGunSpeed => Lang.T("Sentry speed", "Скорость турелей"),
        AgentModifier.SentryGunDamage => Lang.T("Sentry damage", "Урон турелей"),
        AgentModifier.TripMineDamage => Lang.T("Mine damage", "Урон мин"),
        AgentModifier.ScannerRechargeSpeed => Lang.T("Bio-tracker recharge", "Перезарядка биотрекера"),
        AgentModifier.AmmoSupport => Lang.T("Ammo given", "Отдача патронов"),
        AgentModifier.HackingProficiency => Lang.T("Hacking", "Взлом"),
        AgentModifier.ComputerProcessingSpeed => Lang.T("Terminal speed", "Скорость терминалов"),
        AgentModifier.InitialAmmoStandard => Lang.T("Starting main ammo", "Начальные патроны"),
        AgentModifier.InitialAmmoSpecial => Lang.T("Starting special ammo", "Начальные спецпатроны"),
        AgentModifier.InitialAmmoTool => Lang.T("Starting tool ammo", "Начальный запас инструмента"),
        AgentModifier.FogRepellerEffect => Lang.T("Fog repeller", "Отпугиватель тумана"),
        AgentModifier.GlowstickEffect => Lang.T("Glow sticks", "Химсвет"),
        AgentModifier.BioscanSpeed => Lang.T("Scan speed", "Скорость сканов"),
        AgentModifier.MovementSpeed => Lang.T("Movement speed", "Скорость бега"),
        AgentModifier.MovementAcceleration => Lang.T("Acceleration", "Ускорение"),
        _ => m.ToString(),
    };

    private static string EffectLine(AgentModifier m, float delta)
    {
        bool good = AgentModifierManager.ModifierHasInvertedMeaning(m) ? delta < 0f : delta > 0f;
        return $"<color={(good ? "#8fe08f" : "#e08f8f")}>{EffectName(m)} {delta * 100f:+0;-0}%</color>";
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
            boost.Effects.Add((eff.Effect, delta, good));
            ModInfo.Log.LogInfo($"[Syringes] {eff.Effect}: value {value:0.###} → change {delta:+0.###;-0.###} (id {id})");
        }
        if (boost.Ids.Count == 0) return;
        _active.Add(boost);
        ModInfo.Log.LogInfo($"[Syringes] injected '{boost.Name}' for {_minutes.Value:0.#} min");
        try { PlayerChatManager.PostChatMessageLocaly(SNet.LocalPlayer, $"<color=#ffd34f>{Lang.T("Booster syringe", "Шприц-усилитель")}:</color> {string.Join(", ", boost.Effects.Select(e => EffectLine(e.Mod, e.Delta)))}"); } catch { }
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
    private static float _nextHudFind;

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
        if (_onMap.Value) UpdateMap();
    }

    private static TMPro.TextMeshPro _map;

    /// <summary>
    /// Map screen, left, above the legend: "BOOSTER" and one line per effect — name, total change, time left.
    /// Effects of several syringes add up (opposite ones cancel out); each keeps its own time.
    /// </summary>
    private static void UpdateMap()
    {
        var page = CellMenu.CM_PageMap.Current;
        if (page == null || !page.m_isActive) return;
        if (_map == null)
        {
            var src = page.m_expeditionTime;
            var legend = page.m_mapLegend;
            if (src == null || legend == null) return;
            var go = UnityEngine.Object.Instantiate(src.gameObject, legend.transform.parent, false);
            go.name = "GtfoPunk_MapEffects";
            _map = go.GetComponent<TMPro.TextMeshPro>();
            if (_map == null) { UnityEngine.Object.Destroy(go); return; }
            _map.alignment = TMPro.TextAlignmentOptions.BottomLeft;
            _map.enableWordWrapping = false;
            _map.rectTransform.pivot = new Vector2(0f, 0f);
            // Bottom-left corner just above the legend's top-left.
            float top = float.MinValue, left = float.MaxValue, z = legend.transform.position.z;
            foreach (var r in legend.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                top = Math.Max(top, r.bounds.max.y);
                left = Math.Min(left, r.bounds.min.x);
            }
            if (top == float.MinValue) { top = legend.transform.position.y; left = legend.transform.position.x; }
            float gap = (top - legend.transform.position.y) * 0.15f;
            go.transform.position = new Vector3(left, top + Math.Max(0f, gap), z);
            ModInfo.Log.LogInfo($"[Syringes] map effects text added above the legend");
        }
        var sb = new System.Text.StringBuilder();
        if (_active.Count > 0)
        {
            sb.Append("<color=#ffd34f>").Append(Lang.T("BOOSTER", "БУСТЕР")).Append("</color>\n");
            var sum = new Dictionary<AgentModifier, (float Delta, float Until)>();
            foreach (var b in _active)
                foreach (var e in b.Effects)
                {
                    sum.TryGetValue(e.Mod, out var cur);
                    sum[e.Mod] = (cur.Delta + e.Delta, Math.Max(cur.Until, b.Until));
                }
            foreach (var kv in sum.OrderBy(k => EffectName(k.Key)))
            {
                int left = Mathf.CeilToInt(kv.Value.Until - Time.time);
                if (Math.Abs(kv.Value.Delta) < 0.005f) sb.Append($"<color=#9a9a9a>{EffectName(kv.Key)} 0%</color>");
                else sb.Append(EffectLine(kv.Key, kv.Value.Delta));
                sb.Append($"  <color=#cfcfcf>{left / 60}:{left % 60:00}</color>\n");
            }
        }
        float infection = 0f;
        try { infection = PlayerManager.GetLocalPlayerAgent()?.Damage?.Infection ?? 0f; } catch { }
        if (infection > 0.005f) sb.Append($"<color=#9fe07a>{Lang.T("INFECTION", "ИНФЕКЦИЯ")} {infection * 100f:0}%</color>");
        string text = sb.ToString().TrimEnd('\n');
        if (_map.text != text) _map.SetText(text);
    }

    /// <summary>A copy of the watermark text, a bit higher, listing the active syringe effects and time left.</summary>
    private static void UpdateHud()
    {
        if (_hud == null)
        {
            if (_active.Count == 0 || Time.time < _nextHudFind) return;
            _nextHudFind = Time.time + 2f;
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
                sb.Append($"<color=#ffd34f>{left / 60}:{left % 60:00}</color> ").Append(string.Join("  ", b.Effects.Select(e => EffectLine(e.Mod, e.Delta)))).Append('\n');
            }
            text = sb.ToString().TrimEnd('\n');
        }
        if (_hud.text != text) _hud.SetText(text);
    }
}
