using System;
using System.Collections.Generic;
using Agents;
using Enemies;
using Gear;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// «DRED SILENZIO GP6-S»: бесшумный пистолет из «гаража». Урон от Shelling Arid 5, звук от S49,
/// корпус и ствол собираются из деталей разных пушек (настройка Guns). Выстрел будит спящих только в радиусе HardRadius (10 м),
/// дальше, до SoftRadius, только тревожит (пульс), ещё дальше — тишина. Через стены не слышно.
/// Шум считает хост, поэтому мод нужен у всех.
/// </summary>
public class SilencedGun : ModuleBase
{
    public override string Id => "SilencedGun";
    public override string Description => "Бесшумный пистолет DRED SILENZIO GP6-S: урон Arid 5, звук S49, корпус 3RB + верх Mastaba. Будит только вблизи. Нужен у всех.";
    public override ModuleSide Side => ModuleSide.Everyone;
    public override IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    private static BepInEx.Configuration.ConfigEntry<string> _stats, _sound, _guns;
    private static BepInEx.Configuration.ConfigEntry<float> _hard, _soft;
    private static BepInEx.Configuration.ConfigEntry<bool> _softPulse;

    protected override void OnBindConfig()
    {
        _stats = Bind("StatsFrom", "Shelling Arid 5", "От какого оружия урон, магазин, темп (категория).");
        _sound = Bind("SoundFrom", "Shelling S49", "От какого оружия звук выстрела.");
        _guns = Bind("SilencedGuns", NewDefault,
            "Бесшумные пушки через «;»: «Название: Корпус + деталь=откуда, …». Корпус — оружие, чьи детали берутся за основу; 12 — ствол/верх. Детали и номера — в GtfoPunk.GearList.txt.");
        _hard = Bind("HardRadius", 10f, "Радиус (м), в котором выстрел будит врагов.");
        _soft = Bind("SoftRadius", 25f, "Радиус (м), в котором выстрел только тревожит (пульс), но не будит. 0 — не тревожить.");
        _softPulse = Bind("SoftIsPulseOnly", true, "true — дальние враги только «пульсируют»; false — получают обычный шум (могут проснуться при повторных выстрелах).");
    }

    // 4 (ItemFPSSettings) — анимации рук: берём от Mastaba, иначе при перезарядке «пропадает» рука, вставляющая патроны.
    private const string OldDefault = "AM DRED GP1-S: Bataldo 3RB + 12=Mastaba R66";
    private const string NewDefault = "DRED SILENZIO GP6-S: Bataldo 3RB + 12=Mastaba R66, 4=Mastaba R66";

    private static readonly HashSet<uint> _categories = new();
    private static readonly Dictionary<IntPtr, float> _cooldown = new();

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnLevelExit += () => _cooldown.Clear();
    }

    private static void Inject()
    {
        if (_guns.Value == OldDefault) _guns.Value = NewDefault; // обновляем старое значение по умолчанию
        foreach (var old in new[] { "AM DRED GP1-S:", "DRED SILENZIO GP7-S:" }) // переименования
            if (_guns.Value.Contains(old)) _guns.Value = _guns.Value.Replace(old, "DRED SILENZIO GP6-S:");
        var statsGear = GearFactory.FindByName(_stats.Value);
        if (statsGear == null) { ModInfo.Log.LogWarning($"[SilencedGun] не нашёл оружие «{_stats.Value}» — см. GtfoPunk.GearList.txt"); return; }
        var sc = GearFactory.GetComps(statsGear.GearJSON);
        if (!sc.TryGetValue((int)eGearComponent.Category, out var statsCat)) return;
        uint catId = GearFactory.CloneCategory(statsCat, "GtfoPunk_SilencedGun", "DRED SILENZIO",
            Lang.T("Silenced. Wakes enemies only within 10 m; farther ones only stir.", "С глушителем. Будит только в 10 м, дальше враги лишь тревожатся."));
        _categories.Add(catId);

        uint? sound = null;
        var soundGear = GearFactory.FindByName(_sound.Value);
        if (soundGear != null && GearFactory.GetComps(soundGear.GearJSON).TryGetValue((int)eGearComponent.AudioSetting, out var snd)) sound = snd;
        else ModInfo.Log.LogWarning($"[SilencedGun] звук «{_sound.Value}» не найден — звук корпуса");

        int idx = 0;
        foreach (var entry in _guns.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            idx++;
            var nameSplit = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (nameSplit.Length != 2) { ModInfo.Log.LogWarning($"[SilencedGun] не понял «{entry}»"); continue; }
            var bodySplit = nameSplit[1].Split('+', 2, StringSplitOptions.TrimEntries);
            var body = GearFactory.FindByName(bodySplit[0]);
            if (body == null) { ModInfo.Log.LogWarning($"[SilencedGun] корпус «{bodySplit[0]}» не найден"); continue; }

            var comps = new Dictionary<int, uint?>
            {
                [(int)eGearComponent.Category] = catId,
                // Режим огня и тип предмета — как у оружия-донора статов (иначе категория не найдёт свой режим).
                [(int)eGearComponent.FireMode] = sc.TryGetValue((int)eGearComponent.FireMode, out var fm) ? fm : null,
                [(int)eGearComponent.BaseItem] = sc.TryGetValue((int)eGearComponent.BaseItem, out var bi) ? bi : null,
            };
            if (sound.HasValue) comps[(int)eGearComponent.AudioSetting] = sound;
            if (bodySplit.Length > 1)
                foreach (var kv in GearFactory.ParseParts(bodySplit[1], "SilencedGun")) comps[kv.Key] = kv.Value;
            GearFactory.AddGear(body, $"GtfoPunk_SilencedGun_{idx}", nameSplit[0], comps);
        }
    }

    private static bool IsOurs(BulletWeapon w)
    {
        if (w == null || _categories.Count == 0) return false;
        try { var g = w.GearIDRange; return g != null && _categories.Contains(g.GetCompID(eGearComponent.Category)); }
        catch { return false; }
    }

    // Свой выстрел (у стреляющего) и чужой выстрел (у хоста — «синхронизированная» копия оружия).
    [HarmonyPatch(typeof(BulletWeapon), nameof(BulletWeapon.Fire))]
    private static class Patch_Fire { private static void Postfix(BulletWeapon __instance) => OnFire(__instance); }

    [HarmonyPatch(typeof(BulletWeaponSynced), nameof(BulletWeaponSynced.Fire))]
    private static class Patch_FireSynced { private static void Postfix(BulletWeaponSynced __instance) => OnFire(__instance); }

    private static void OnFire(BulletWeapon w)
    {
        if (!IsOurs(w)) return;
        var owner = w.Owner;
        if (owner == null) return;
        owner.m_noise = Agent.NoiseType.Silent; // штатный «громкий выстрел» не нужен
        if (!SNet.IsMaster) return;
        if (_cooldown.TryGetValue(owner.Pointer, out var t) && Time.time < t) return;
        _cooldown[owner.Pointer] = Time.time + 0.4f;
        try { MakeNoise(owner); } catch (Exception e) { ModInfo.Log.LogWarning($"[SilencedGun] {e.Message}"); }
    }

    private static void MakeNoise(PlayerAgent owner)
    {
        float hard = _hard.Value, soft = Math.Max(_soft.Value, hard);
        if (soft <= 0f) return;
        var maker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>();
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(owner.Position, soft, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var limb = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>();
            var enemy = limb?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            // Через стены и закрытые двери не слышно.
            if (Physics.Linecast(owner.EyePosition, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
            var node = enemy.CourseNode;
            if (node == null) continue;
            if (node.m_zone.LocalIndex != GameData.eLocalZoneIndex.Zone_0 && node.m_zone.m_sourceGate != null && !node.m_zone.m_sourceGate.HasBeenOpenedDuringPlay) continue;

            bool wake = Vector3.Distance(owner.EyePosition, enemy.EyePosition) <= hard;
            NoiseManager.MakeNoise(new NM_NoiseData
            {
                noiseMaker = maker,
                position = enemy.Position,
                node = node,
                radiusMin = 0.01f,
                radiusMax = 1f,
                yScale = 1f,
                type = wake ? NM_NoiseType.InstaDetect : (_softPulse.Value ? NM_NoiseType.PulseOnly : NM_NoiseType.Detectable),
                includeToNeightbourAreas = false,
                raycastFirstNode = false,
            });
        }
    }
}
