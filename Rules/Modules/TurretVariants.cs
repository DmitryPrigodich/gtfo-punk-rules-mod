using System;
using System.Collections.Generic;
using System.Linq;
using Enemies;
using System.Reflection;
using System.Text.Json.Nodes;
using GameData;
using Gear;
using GtfoPunk.Core;
using GtfoPunk.Rules.Core;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Localization;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Новые турели в слоте инструмента (выбираются в лоадауте, как обычная турель):
///  - FT1 анти-туманная: не стреляет, вокруг себя держит сферу без тумана (как отпугиватель, но бесконечно).
///  - LT2 фонарная: ставится в одну сторону, светит прожектором и поворачивается за врагами в своём секторе;
///    когда видит врага — светит ярче. Не стреляет.
///  - BT3 турель-биотрекер: не стреляет, раз в несколько секунд сканирует свой сектор и метит врагов (как биотрекер).
///  - CF4 пенострел: вместо пуль стреляет сгустками пены (как пеногун) в цель, тратит патроны турели.
/// Делается клонированием штатной турели в таблицах игры, поэтому мод должен стоять у ВСЕХ игроков.
/// </summary>
public class TurretVariants : ModuleBase
{
    public override string Id => "TurretVariants";
    public override string Description => "Новые инструменты: DRED GARAGE FT1 (анти-туман), LT2 (прожектор), BT3 (биотрекер), CF4 (пенострел). Нужен у всех игроков.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static BepInEx.Configuration.ConfigEntry<bool> _fog, _light;
    private static BepInEx.Configuration.ConfigEntry<float> _fogRange, _lightRange, _lightAngle, _lightIntensity, _disturbInterval;
    private static BepInEx.Configuration.ConfigEntry<string> _disturb, _lampItem, _lampOffset, _lampRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _lampScale, _lightForward, _fogLamp, _fogModelScale;
    private static BepInEx.Configuration.ConfigEntry<bool> _hideGun, _turbine;
    private static BepInEx.Configuration.ConfigEntry<float> _turbineScale, _turbineRange;
    private static BepInEx.Configuration.ConfigEntry<string> _turbineOffset;
    private static BepInEx.Configuration.ConfigEntry<string> _fogModelOffset;
    private static BepInEx.Configuration.ConfigEntry<bool> _tracker, _trackerWalls, _foam, _foamSkipGlued;
    private static BepInEx.Configuration.ConfigEntry<float> _trackerRange, _trackerAngle, _trackerInterval;
    private static BepInEx.Configuration.ConfigEntry<float> _foamInterval, _foamSpeed, _foamGravity, _foamAmmo, _foamExpand, _foamRange;

    protected override void OnBindConfig()
    {
        _fog = Bind("FogTurret", true, "Добавить анти-туманную турель.");
        _fogRange = Bind("FogRange", 8f, "Радиус чистой от тумана сферы (м).");
        _light = Bind("LightTurret", true, "Добавить фонарную турель.");
        _lightRange = Bind("LightRange", 30f, "Дальность прожектора (м).");
        _lightAngle = Bind("LightAngle", 45f, "Угол луча прожектора (градусы).");
        _lightIntensity = Bind("LightIntensity2", 1.6f, "Яркость прожектора (при враге в секторе — в 1.6 раза ярче).");
        _disturb = Bind("LightDisturbs", "Detectable", "Как свет действует на врагов в луче: Detectable — тревожит (может разбудить, как шаги рядом), PulseOnly — только пульс, Off — никак.");
        _disturbInterval = Bind("LightDisturbInterval", 1.5f, "Как часто (сек) свет «тревожит» врагов в луче.");
        _lampItem = Bind("LampModel", "Long Range Flashlight", "Предмет, модель которого ставится на фонарную турель (имя или часть имени; пусто — без модели).");
        _lampOffset = Bind("LampOffset", "0,0.08,0.05", "Смещение модели фонаря относительно ствола турели (x,y,z, м).");
        _lampRotation = Bind("LampRotation3", "0,270,0", "Поворот модели фонаря (градусы x,y,z), если смотрит не туда.");
        _lampScale = Bind("LampScale", 1f, "Масштаб модели фонаря.");
        _lightForward = Bind("LightForward", 0.35f, "Насколько вынести источник света вперёд от модели фонаря (м), чтобы он не засвечивал сам фонарь.");
        _hideGun = Bind("HideGun", true, "Прятать ствол автопушки у обеих турелей (остаётся тренога с корпусом).");
        _fogModelOffset = Bind("FogModelOffset", "0,0.25,0", "Где на FT1 стоит модель отпугивателя (x,y,z, м от верха турели).");
        _fogModelScale = Bind("FogModelScale", 1f, "Масштаб модели отпугивателя на FT1.");
        _turbine = Bind("FogTurbineTurret", true, "Тестовая турель DRED GARAGE FT1-T: уменьшенная большая турбина-отпугиватель.");
        _turbineScale = Bind("TurbineScale", 0.3f, "Масштаб модели турбины на FT1-T.");
        _turbineOffset = Bind("TurbineOffset", "0,0.05,0", "Где на FT1-T стоит турбина (x,y,z, м от верха турели).");
        _turbineRange = Bind("TurbineRange", 5f, "Радиус сферы без тумана у FT1-T (м).");
        _fogLamp = Bind("FogLamp", 0.5f, "Яркость тусклой лампочки на FT1 (как у светящейся палки). 0 — без лампочки.");
        _tracker = Bind("TrackerTurret", true, "Добавить турель-биотрекер DRED GARAGE BT3 (метит врагов в своём секторе, не стреляет).");
        _trackerRange = Bind("TrackerRange", 25f, "Дальность сканирования BT3 (м).");
        _trackerAngle = Bind("TrackerAngle", 90f, "Ширина сектора BT3 (градусы, по горизонтали, от направления установки).");
        _trackerInterval = Bind("TrackerInterval", 3f, "Как часто BT3 сканирует сектор (сек).");
        _trackerWalls = Bind("TrackerThroughWalls", true, "BT3 видит сквозь стены (как ручной биотрекер). false — только в прямой видимости.");
        _foam = Bind("FoamTurret", true, "Добавить пенострел DRED GARAGE CF4 (стреляет пеной вместо пуль).");
        _foamInterval = Bind("FoamInterval", 1.2f, "Пауза между выстрелами пеной (сек).");
        _foamSpeed = Bind("FoamSpeed", 20f, "Начальная скорость сгустка пены (м/с).");
        _foamGravity = Bind("FoamGravity", 9.81f, "Гравитация для расчёта навесной траектории (м/с²). Настоящая пишется в лог при первом выстреле.");
        _foamAmmo = Bind("FoamAmmoPerShot", 6f, "Сколько «пуль» турели стоит один сгусток пены.");
        _foamExpand = Bind("FoamExpand", -1f, "Объём расширения сгустка (-1 — как у пеногуна по умолчанию).");
        _foamRange = Bind("FoamRange", 20f, "Дальше этого (м) пенострел не стреляет.");
        _foamSkipGlued = Bind("FoamSkipGlued", true, "Не тратить пену на врага, который уже залит и обездвижен.");
    }

    private enum Mode { Fog, Light, FogTurbine, Tracker, Foam }

    /// <summary>Режимы, которые крутятся за врагами и сканируют штатно (остальные стоят неподвижно).</summary>
    private static bool Tracks(Mode m) => m == Mode.Light || m == Mode.Tracker || m == Mode.Foam;
    /// <summary>Режимы, у которых прячется ствол автопушки.</summary>
    private static bool HidesGun(Mode m) => m == Mode.Light || m == Mode.Fog || m == Mode.FogTurbine;
    private const uint FogTurbineItemId = 133; // Carry_HeavyFogRepeller — большая турбина из миссий

    private const uint SentryCategoryId = 12;
    private const uint FogRepellerItemId = 117; // CONSUMABLE_FogRepeller

    private static readonly Dictionary<uint, Mode> _modeByCategory = new();

    private class Turret
    {
        public SentryGunInstance Inst;
        public Mode Mode;
        public bool Ready;
        public float GiveUpAt;
        public GameObject Extra;
        public Light Light;
        public FogRepeller_Sphere Sphere;
        public float NextDisturb, SpawnedAt, NextHide;
        public TMPro.TextMeshPro Screen;
        public float NextScan, NextShot;
        public int Tagged;
    }

    private static readonly Dictionary<IntPtr, Turret> _turrets = new();

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _turrets.Clear(); _fogDumpAt = -1f; _fogDumped = false; };
        GearFactory.OnInject += Inject;
    }

    // ---------- добавление инструментов в таблицы игры ----------

    private static void Inject()
    {
        var baseGear = GearFactory.FindByCategory(SentryCategoryId, b => GearFactory.NameOf(b).IndexOf("Burst", StringComparison.OrdinalIgnoreCase) >= 0);
        if (baseGear == null) { ModInfo.Log.LogWarning("[TurretVariants] не нашёл штатную турель в данных игры — новые турели не добавлены"); return; }
        if (_fog.Value) AddVariant(baseGear, Mode.Fog, "GtfoPunk_FogTurret", "DRED GARAGE FT1",
            Lang.T("Stationary fog repeller. Keeps a fog-free sphere around itself. Does not shoot.", "Стационарный отпугиватель тумана. Держит вокруг себя сферу без тумана. Не стреляет."));
        if (_light.Value) AddVariant(baseGear, Mode.Light, "GtfoPunk_LightTurret", "DRED GARAGE LT2",
            Lang.T("Searchlight that tracks enemies in its sector. Does not shoot.", "Прожектор, который поворачивается за врагами в своём секторе. Не стреляет."));
        // Тестовая: уменьшенная копия большой турбины-отпугивателя. Добавляется последней, чтобы не сдвигать номера остальных.
        if (_turbine.Value) AddVariant(baseGear, Mode.FogTurbine, "GtfoPunk_FogTurbineTurret", "DRED GARAGE FT1-T",
            Lang.T("TEST: miniature fog repeller turbine. Smaller fog-free sphere. Does not shoot.", "ТЕСТ: уменьшенная турбина-отпугиватель. Сфера без тумана поменьше. Не стреляет."));
        // Новые — строго в конец, чтобы не сдвигать номера уже существующих.
        if (_tracker.Value) AddVariant(baseGear, Mode.Tracker, "GtfoPunk_TrackerTurret", "DRED GARAGE BT3",
            Lang.T("Bio-tracker turret. Scans its sector every few seconds and tags enemies, even through walls. Does not shoot.",
                   "Турель-биотрекер. Раз в несколько секунд сканирует свой сектор и метит врагов, даже за стенами. Не стреляет."));
        if (_foam.Value) AddVariant(baseGear, Mode.Foam, "GtfoPunk_FoamTurret", "DRED GARAGE CF4",
            Lang.T("C-foam sentry. Lobs foam globs at enemies instead of bullets, gluing them in place. Uses sentry ammo.",
                   "Пенострел. Вместо пуль закидывает врагов сгустками пены и приклеивает их. Тратит патроны турели."));
    }

    private static void AddVariant(PlayerOfflineGearDataBlock baseGear, Mode mode, string key, string title, string desc)
    {
        uint catId = GearFactory.CloneCategory(SentryCategoryId, key, title, desc);
        GearFactory.AddGear(baseGear, key, title, new Dictionary<int, uint?> { [(int)eGearComponent.Category] = catId });
        _modeByCategory[catId] = mode;
    }

    // ---------- поведение поставленной турели ----------

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnSpawn))]
    private static class Patch_Spawn
    {
        private static void Postfix(SentryGunInstance __instance, pGearSpawnData spawnData)
        {
            try
            {
                var gear = new GearIDRange(spawnData.gearIDRange);
                if (!_modeByCategory.TryGetValue(gear.GetCompID(eGearComponent.Category), out var mode)) return;
                _turrets[__instance.Pointer] = new Turret { Inst = __instance, Mode = mode, GiveUpAt = Time.time + 10f, SpawnedAt = Time.time };
                ModInfo.Log.LogInfo($"[TurretVariants] поставлена турель {mode}");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] OnSpawn: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.OnDespawn))]
    private static class Patch_Despawn
    {
        private static void Prefix(SentryGunInstance __instance)
        {
            if (!_turrets.TryGetValue(__instance.Pointer, out var t)) return;
            try { t.Sphere?.KillRepellerInstantly(); } catch { }
            if (t.Extra != null) UnityEngine.Object.Destroy(t.Extra);
            _turrets.Remove(__instance.Pointer);
        }
    }

    private static Turret Of(SentryGunInstance_Firing_Bullets firing)
    {
        var core = firing?.m_core;
        var inst = core == null ? null : ((Il2CppObjectBase)core).TryCast<SentryGunInstance>();
        return inst != null && _turrets.TryGetValue(inst.Pointer, out var t) ? t : null;
    }

    // Наши турели не стреляют.
    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireMaster))]
    private static class Patch_FireMaster { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    [HarmonyPatch(typeof(SentryGunInstance_Firing_Bullets), nameof(SentryGunInstance_Firing_Bullets.UpdateFireClient))]
    private static class Patch_FireClient { private static bool Prefix(SentryGunInstance_Firing_Bullets __instance) => Of(__instance) == null; }

    // Анти-туманная не вертится за врагами.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.UpdateRotation))]
    private static class Patch_Rotation
    {
        private static bool Prefix(SentryGunInstance __instance) =>
            !(_turrets.TryGetValue(__instance.Pointer, out var t) && !Tracks(t.Mode));
    }

    // FT1 ничего не ищет: без сканирующего луча и без поворотов.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToScan))]
    private static class Patch_Scan
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (_turrets.TryGetValue(__instance.Pointer, out var t) && !Tracks(t.Mode)) { __result = false; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToFire))]
    private static class Patch_WantFire
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (_turrets.ContainsKey(__instance.Pointer)) { __result = false; return false; }
            return true;
        }
    }

    /// <summary>Прячет ствол и ствольную коробку автопушки (только отрисовку — точки крепления остаются).</summary>
    private static void HideGun(SentryGunInstance inst)
    {
        if (!_hideGun.Value) return;
        int hidden = 0;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            for (var tr = r.transform; tr != null && tr != inst.transform; tr = tr.parent)
            {
                string n = tr.name;
                if (n.StartsWith("GtfoPunk_")) break;
                if (n.StartsWith("Front_") || n.StartsWith("Receiver_")) { if (r.enabled) hidden++; r.enabled = false; break; }
            }
        }
        if (hidden > 0) ModInfo.Log.LogInfo($"[TurretVariants] спрятано деталей ствола: {hidden}");
    }

    private static readonly List<IntPtr> _dead = new();

    private static float _fogDumpAt = -1f;
    private static bool _fogDumped;

    private static void Tick()
    {
        // Раз за уровень, через 5 с после высадки: настоящие параметры отпугивателей тумана — в лог и файл.
        if (!_fogDumped && GameUtil.InLevel)
        {
            if (_fogDumpAt < 0f) _fogDumpAt = Time.time + 5f;
            else if (Time.time >= _fogDumpAt) { _fogDumped = true; try { DumpFogRepellers(); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] замер тумана: {e.Message}"); } }
        }
        if (_turrets.Count == 0) return;
        _dead.Clear();
        foreach (var kv in _turrets)
        {
            var t = kv.Value;
            if (t.Inst == null) { _dead.Add(kv.Key); continue; }
            try
            {
                if (!t.Ready)
                {
                    t.Ready = t.Mode switch { Mode.Light => SetupLight(t), Mode.Tracker or Mode.Foam => true, _ => SetupFog(t) };
                    if (!t.Ready && Time.time > t.GiveUpAt) { ModInfo.Log.LogWarning($"[TurretVariants] не удалось настроить турель ({t.Mode})"); t.Ready = true; }
                    else if (t.Ready) ModInfo.Log.LogInfo($"[TurretVariants] турель {t.Mode} настроена");
                }
                else
                {
                    UpdateScreen(t);
                    // Детали турели могут досоздаваться позже — несколько секунд повторяем «спрятать ствол».
                    if (HidesGun(t.Mode) && Time.time - t.SpawnedAt < 8f && Time.time >= t.NextHide) { t.NextHide = Time.time + 0.5f; HideGun(t.Inst); }
                    if (t.Mode == Mode.Tracker && SNetwork.SNet.IsMaster && Time.time >= t.NextScan)
                    {
                        t.NextScan = Time.time + Math.Max(0.5f, _trackerInterval.Value);
                        TrackerScan(t);
                    }
                    if (t.Mode == Mode.Foam && SNetwork.SNet.IsMaster && Time.time >= t.NextShot) FoamFire(t);
                    if (t.Mode == Mode.Light && t.Light != null)
                    {
                        bool target = t.Inst.m_detection != null && t.Inst.m_detection.HasTarget;
                        t.Light.intensity = _lightIntensity.Value * (target ? 1.6f : 1f);
                        if (SNetwork.SNet.IsMaster && Time.time >= t.NextDisturb)
                        {
                            t.NextDisturb = Time.time + Math.Max(0.3f, _disturbInterval.Value);
                            Disturb(t);
                        }
                    }
                }
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] {e.Message}"); t.Ready = true; }
        }
        foreach (var k in _dead) _turrets.Remove(k);
    }

    private static bool SetupLight(Turret t)
    {
        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        var align = firing?.MuzzleAlign;
        if (align == null) return false;

        // Точка ствола (MuzzleAlign) — на конце спрятанного ствола, далеко впереди. Фонарь ставим у основания ствола:
        // на корне детали Front_*, которая поворачивается вместе с головой турели; направление — как у ствола.
        // Front_M — сама точка крепления; берём самого верхнего предка с именем Front_* (корень детали ствола).
        Transform root = align.parent ?? align;
        var chain = new List<string>();
        for (var tr = align.parent; tr != null && tr != t.Inst.transform; tr = tr.parent)
        {
            chain.Add(tr.name);
            if (tr.name.StartsWith("Front_")) root = tr;
        }
        ModInfo.Log.LogInfo($"[TurretVariants] цепочка от ствола: {string.Join(" > ", chain)}");
        var head = new GameObject("GtfoPunk_Head");
        head.transform.SetParent(root, false);
        head.transform.position = root.position;
        head.transform.rotation = align.rotation;

        var go = new GameObject("GtfoPunk_Searchlight");
        go.transform.SetParent(head.transform, false);
        go.transform.localRotation = Quaternion.identity;
        try { AttachLamp(head.transform); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] модель фонаря: {e.Message}"); }
        go.transform.localPosition = Vec(_lampOffset.Value, Vector3.zero) + Vector3.forward * _lightForward.Value; // свет перед стеклом фонаря, а не внутри модели
        HideGun(t.Inst);
        ModInfo.Log.LogInfo($"[TurretVariants] фонарь крепится к «{root.name}», до точки ствола {Vector3.Distance(root.position, align.position):0.00} м");
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.range = _lightRange.Value;
        light.spotAngle = _lightAngle.Value;
        light.intensity = _lightIntensity.Value;
        light.color = new Color(1f, 0.95f, 0.85f);
        light.shadows = LightShadows.None;
        CL_Light.Create(eClusteredLightType.Spot, go, light); // GTFO рисует свет через свою кластерную систему

        t.Extra = go;
        t.Light = light;
        return true;
    }

    private static bool SetupFog(Turret t)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null || perMode.Length <= (int)ItemMode.Instance) return false;
        var dict = perMode[(int)ItemMode.Instance];
        if (dict == null || !dict.ContainsKey(FogRepellerItemId)) return false;
        var list = dict[FogRepellerItemId];
        if (list == null || list.Count == 0 || list[0] == null) return false;

        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        // Ждём, пока турель соберёт свои детали (иначе нечего прятать и некуда крепить).
        if (firing?.MuzzleAlign == null && Time.time - t.SpawnedAt < 4f) return false;
        var top = firing?.MuzzleAlign != null ? firing.MuzzleAlign.position : t.Inst.transform.position + Vector3.up * 0.6f;
        var go = UnityEngine.Object.Instantiate(list[0], top, Quaternion.identity);
        go.transform.SetParent(t.Inst.transform, true);
        go.name = "GtfoPunk_FogSphere";
        HideGun(t.Inst);
        // Модель отпугивателя сверху (копия мешей) и тусклая лампочка.
        try
        {
            var anchor = new GameObject("GtfoPunk_FogTop");
            anchor.transform.SetParent(t.Inst.transform, false);
            anchor.transform.position = top;
            // Ровно по вертикали, даже если турель стоит на склоне; поворот — только по направлению турели.
            anchor.transform.rotation = Quaternion.Euler(0f, t.Inst.transform.eulerAngles.y, 0f);
            if (t.Mode == Mode.FogTurbine)
                AttachModel(anchor.transform, FogTurbineItemId, Vec(_turbineOffset.Value, Vector3.zero), Vector3.zero, _turbineScale.Value, "GtfoPunk_FogModel");
            else
                AttachModel(anchor.transform, FogRepellerItemId, Vec(_fogModelOffset.Value, Vector3.zero), Vector3.zero, _fogModelScale.Value, "GtfoPunk_FogModel");
            if (_fogLamp.Value > 0f)
            {
                var lgo = new GameObject("GtfoPunk_FogLamp");
                lgo.transform.SetParent(anchor.transform, false);
                lgo.transform.localPosition = Vec(_fogModelOffset.Value, Vector3.zero) + Vector3.up * 0.15f;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 5f;
                l.intensity = _fogLamp.Value;
                l.color = new Color(1f, 0.75f, 0.45f); // тёплая «лампочка Ильича»
                l.shadows = LightShadows.None;
                CL_Light.Create(eClusteredLightType.Point, lgo, l);
            }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] модель FT1: {e.Message}"); }

        // От брошенного отпугивателя оставляем только сферу: без физики, коллайдеров и модельки.
        foreach (var c in go.GetComponentsInChildren<FogRepellerInstance>(true)) UnityEngine.Object.Destroy(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        var sphere = go.GetComponentInChildren<FogRepeller_Sphere>(true);
        if (sphere == null) { UnityEngine.Object.Destroy(go); return false; }
        sphere.InfiniteDuration = true;
        sphere.Range = t.Mode == Mode.FogTurbine ? _turbineRange.Value : _fogRange.Value;
        sphere.gameObject.SetActive(true);
        sphere.StartRepelling();

        t.Extra = go;
        t.Sphere = sphere;
        return true;
    }

    // ---------- экран, «тревога» от света, модель фонаря ----------

    private static void UpdateScreen(Turret t)
    {
        if (t.Screen == null)
        {
            var scr = t.Inst.m_screen == null ? null : ((Il2CppObjectBase)t.Inst.m_screen).TryCast<SentryGunScreen>();
            t.Screen = scr?.m_ammoText;
            if (t.Screen == null) return;
        }
        if (t.Mode == Mode.Foam) return; // у пенострела на экране обычный счётчик патронов
        string text = t.Mode switch { Mode.Light => "LT2", Mode.FogTurbine => "FT1-T", Mode.Tracker => t.Tagged > 0 ? $"BT3 {t.Tagged}" : "BT3", _ => "FT1" };
        if (t.Screen.text != text) t.Screen.text = text;
    }

    private static void Disturb(Turret t)
    {
        if (!Enum.TryParse<NM_NoiseType>(_disturb.Value, true, out var type)) return; // Off
        var tr = t.Light.transform;
        float range = _lightRange.Value, half = _lightAngle.Value * 0.5f;
        var owner = t.Inst.Owner;
        var maker = owner != null ? ((Il2CppObjectBase)owner).TryCast<INM_NoiseMaker>() : ((Il2CppObjectBase)t.Inst).TryCast<INM_NoiseMaker>();
        if (maker == null) return;
        var seen = new HashSet<IntPtr>();
        foreach (var col in Physics.OverlapSphere(tr.position, range, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var to = enemy.EyePosition - tr.position;
            if (Vector3.Angle(tr.forward, to) > half) continue;           // не в луче
            if (Physics.Linecast(tr.position, enemy.EyePosition, LayerManager.MASK_WORLD)) continue; // за стеной
            var node = enemy.CourseNode;
            if (node == null) continue;
            NoiseManager.MakeNoise(new NM_NoiseData
            {
                noiseMaker = maker,
                position = enemy.Position,
                node = node,
                radiusMin = 0.01f,
                radiusMax = 1f,
                yScale = 1f,
                type = type,
                includeToNeightbourAreas = false,
                raycastFirstNode = false,
            });
        }
    }

    // ---------- BT3: сканирование сектора ----------

    private static void TrackerScan(Turret t)
    {
        var origin = t.Inst.transform.position + Vector3.up * 0.6f;
        var fwd = t.Inst.transform.forward; fwd.y = 0f;
        float half = _trackerAngle.Value * 0.5f;
        var seen = new HashSet<IntPtr>();
        int count = 0, tagged = 0;
        foreach (var col in Physics.OverlapSphere(origin, _trackerRange.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
        {
            var enemy = col == null ? null : col.GetComponent<Dam_EnemyDamageLimb>()?.GlueTargetEnemyAgent;
            if (enemy == null || !enemy.Alive || !seen.Add(enemy.Pointer)) continue;
            var to = enemy.Position - origin; to.y = 0f;
            if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.01f && Vector3.Angle(fwd, to) > half) continue; // вне сектора
            if (!_trackerWalls.Value && Physics.Linecast(origin, enemy.EyePosition, LayerManager.MASK_WORLD)) continue;
            count++;
            if (enemy.IsTagged) continue;
            ToolSyncManager.WantToTagEnemy(enemy);
            tagged++;
        }
        t.Tagged = count;
        if (tagged > 0) ModInfo.Log.LogInfo($"[TurretVariants] BT3: в секторе {count}, помечено новых {tagged}");
    }

    // ---------- CF4: стрельба пеной ----------

    private static bool _foamLogged, _glueGravityLogged;

    private static void FoamFire(Turret t)
    {
        var det = t.Inst.m_detection;
        if (det == null || !det.HasTarget) return;
        if (!t.Inst.TryGetTargetAimPos(out var aim)) return;
        var target = det.Target;
        var enemy = target == null ? null : target.GetComponentInParent<EnemyAgent>();
        if (enemy != null && _foamSkipGlued.Value && enemy.Damage != null && enemy.Damage.IsStuckInGlue) return;

        float cost = Math.Max(0f, t.Inst.CostOfBullet * _foamAmmo.Value);
        if (!_foamLogged)
        {
            _foamLogged = true;
            ModInfo.Log.LogInfo($"[TurretVariants] CF4: патроны {t.Inst.Ammo:0.##}/{t.Inst.AmmoMaxCap:0.##}, цена пули {t.Inst.CostOfBullet:0.###}, цена сгустка {cost:0.###}");
        }
        if (t.Inst.m_ammo < cost) return;

        var owner = t.Inst.Owner;
        if (owner == null) return;
        var firing = t.Inst.m_firing == null ? null : ((Il2CppObjectBase)t.Inst.m_firing).TryCast<SentryGunInstance_Firing_Bullets>();
        var muzzle = firing?.MuzzleAlign;
        var from = muzzle != null ? muzzle.position + muzzle.forward * 0.3f : t.Inst.transform.position + Vector3.up * 0.7f;
        if (Vector3.Distance(from, aim) > _foamRange.Value) return;

        var vel = Ballistic(from, aim, Math.Max(1f, _foamSpeed.Value), Math.Max(0f, _foamGravity.Value));
        ProjectileManager.WantToFireGlue(owner, from, vel, _foamExpand.Value, true);
        t.Inst.m_ammo = Math.Max(0f, t.Inst.m_ammo - cost);
        t.NextShot = Time.time + Math.Max(0.2f, _foamInterval.Value);
    }

    /// <summary>Скорость для попадания из from в to при заданной начальной скорости и гравитации (низкая дуга; если не достаёт — 45°).</summary>
    private static Vector3 Ballistic(Vector3 from, Vector3 to, float speed, float g)
    {
        var d = to - from;
        var flat = new Vector3(d.x, 0f, d.z);
        float x = flat.magnitude, y = d.y;
        if (g <= 0.01f || x < 0.05f) return d.normalized * speed;
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
        float angle = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI / 4f;
        var dir = flat / x;
        return (dir * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
    }

    // Один раз пишем в лог настоящую гравитацию сгустка пены — чтобы выставить FoamGravity.
    [HarmonyPatch(typeof(ProjectileManager), nameof(ProjectileManager.SpawnGlueGunProjectileIfNeeded))]
    private static class Patch_GlueSpawn
    {
        private static void Postfix(GlueGunProjectile __result)
        {
            if (_glueGravityLogged || __result == null) return;
            _glueGravityLogged = true;
            try { ModInfo.Log.LogInfo($"[TurretVariants] пена: гравитация сгустка {__result.m_gravity}"); } catch { }
        }
    }

    private static Vector3 Vec(string s, Vector3 def)
    {
        var p = s.Split(',');
        if (p.Length != 3) return def;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out var x) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var y)
            && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var z) ? new Vector3(x, y, z) : def;
    }

    private static uint _lampId = uint.MaxValue;

    private static uint LampItemId()
    {
        if (_lampId != uint.MaxValue) return _lampId;
        _lampId = 0;
        string want = new string(_lampItem.Value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (want.Length == 0) return 0;
        foreach (var b in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
        {
            if (b == null) continue;
            foreach (var n in new[] { b.publicName, b.name })
            {
                string nn = new string((n ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
                if (nn.Length > 0 && nn.Contains(want)) { _lampId = b.persistentID; ModInfo.Log.LogInfo($"[TurretVariants] модель фонаря: {b.publicName} ({b.persistentID})"); return _lampId; }
            }
        }
        ModInfo.Log.LogWarning($"[TurretVariants] предмет «{_lampItem.Value}» не найден — фонарная турель без модели");
        return 0;
    }

    private static GameObject LampPrefab(uint id)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return null;
        foreach (var mode in new[] { ItemMode.Pickup, ItemMode.ThirdPerson, ItemMode.Instance, ItemMode.FirstPerson })
        {
            if (perMode.Length <= (int)mode) continue;
            var dict = perMode[(int)mode];
            if (dict == null || !dict.ContainsKey(id)) continue;
            var list = dict[id];
            // Берём первый вариант модели, в котором реально есть меши (у некоторых предметов модель «от третьего лица» пустая).
            if (list != null && list.Count > 0 && list[0] != null && list[0].GetComponentsInChildren<Renderer>(true).Length > 0) return list[0];
        }
        return null;
    }

    /// <summary>Ставит на турель копию модели фонаря: только меши, без скриптов, физики и коллайдеров.</summary>
    private static void AttachLamp(Transform parent) =>
        AttachModel(parent, LampItemId(), Vec(_lampOffset.Value, Vector3.zero), Vec(_lampRotation.Value, Vector3.zero), _lampScale.Value, "GtfoPunk_Lamp");

    private static void AttachModel(Transform parent, uint id, Vector3 offset, Vector3 euler, float scale, string name)
    {
        if (id == 0) return;
        var prefab = LampPrefab(id);
        if (prefab == null) { ModInfo.Log.LogWarning($"[TurretVariants] модель предмета {id} не загружена"); return; }

        var holder = new GameObject(name);
        holder.SetActive(false); // пока выключен — скрипты копии не запустятся
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = offset;
        holder.transform.localRotation = Quaternion.Euler(euler);
        holder.transform.localScale = Vector3.one * scale;

        var copy = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        for (int pass = 0; pass < 3; pass++) // несколько проходов: часть компонентов держится за другие
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<MeshRenderer>() != null || c.TryCast<SkinnedMeshRenderer>() != null) continue;
                try { UnityEngine.Object.DestroyImmediate(c); } catch { }
            }
        int layer = parent.gameObject.layer;
        foreach (var tr in copy.GetComponentsInChildren<Transform>(true)) { tr.gameObject.layer = layer; tr.gameObject.SetActive(true); }
        int n = 0;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.forceRenderingOff = false; n++; }
        holder.SetActive(true);
        ModInfo.Log.LogInfo($"[TurretVariants] модель {name}: {n} мешей, слой {layer}");
    }

    // ---------- замер настоящих отпугивателей ----------

    private static string Describe(FogRepeller_Sphere f) =>
        $"радиус {f.Range:0.##} м, работает {(f.InfiniteDuration ? "бесконечно" : f.LifeDuration.ToString("0.#") + " с")}, " +
        $"раздувается {f.GrowDuration:0.#} с, сдувается {f.ShrinkDuration:0.#} с, плотность {f.Density:0.##}";

    private static void DumpFogRepellers()
    {
        var lines = new List<string> { $"# Отпугиватели тумана ({DateTime.Now:yyyy-MM-dd HH:mm}), уровень: {RundownManager.ActiveExpedition?.Descriptive?.PublicName}" };
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode != null)
            for (int m = 0; m < perMode.Length; m++)
            {
                var dict = perMode[m];
                if (dict == null) continue;
                foreach (var kv in dict)
                {
                    var list = kv.Value;
                    if (list == null || list.Count == 0 || list[0] == null) continue;
                    foreach (var f in list[0].GetComponentsInChildren<FogRepeller_Sphere>(true))
                    {
                        var item = GameDataBlockBase<ItemDataBlock>.GetBlock(kv.Key);
                        lines.Add($"модель: {item?.publicName ?? kv.Key.ToString()} ({kv.Key}), вид {(ItemMode)m}: {Describe(f)}");
                    }
                }
            }
        foreach (var g in UnityEngine.Object.FindObjectsOfType<HeavyFogRepellerGlobalState>())
            if (g != null && g.m_repellerSphere != null) lines.Add($"на уровне: турбина «{g.gameObject.name}»: {Describe(g.m_repellerSphere)}");
        foreach (var f in UnityEngine.Object.FindObjectsOfType<FogRepeller_Sphere>())
            if (f != null) lines.Add($"на уровне: «{f.gameObject.name}» ({f.CurrentState}, сейчас {f.m_currentRange:0.##} м): {Describe(f)}");
        if (lines.Count == 1) lines.Add("отпугивателей не найдено (модели не загружены на этом уровне)");
        foreach (var l in lines) ModInfo.Log.LogInfo($"[Туман] {l}");
        System.IO.File.AppendAllLines(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "GtfoPunk.FogRepellers.txt"), lines);
    }
}
