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
/// Две новые турели в слоте инструмента (выбираются в лоадауте, как обычная турель):
///  - Анти-туманная: не стреляет, вокруг себя держит сферу без тумана (как отпугиватель, но бесконечно).
///  - Фонарная: ставится в одну сторону, светит прожектором и поворачивается за врагами в своём секторе;
///    когда видит врага — светит ярче. Не стреляет.
/// Делается клонированием штатной турели в таблицах игры, поэтому мод должен стоять у ВСЕХ игроков.
/// </summary>
public class TurretVariants : ModuleBase
{
    public override string Id => "TurretVariants";
    public override string Description => "Новые инструменты: DRED GARAGE FT1 (анти-туманная турель) и DRED GARAGE LT2 (фонарная турель). Нужен у всех игроков.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static BepInEx.Configuration.ConfigEntry<bool> _fog, _light;
    private static BepInEx.Configuration.ConfigEntry<float> _fogRange, _lightRange, _lightAngle, _lightIntensity, _disturbInterval;
    private static BepInEx.Configuration.ConfigEntry<string> _disturb, _lampItem, _lampOffset, _lampRotation;
    private static BepInEx.Configuration.ConfigEntry<float> _lampScale, _lightForward, _fogLamp, _fogModelScale;
    private static BepInEx.Configuration.ConfigEntry<bool> _hideGun;
    private static BepInEx.Configuration.ConfigEntry<string> _fogModelOffset;

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
        _lampRotation = Bind("LampRotation", "0,0,0", "Поворот модели фонаря (градусы x,y,z), если смотрит не туда.");
        _lampScale = Bind("LampScale", 1f, "Масштаб модели фонаря.");
        _lightForward = Bind("LightForward", 0.35f, "Насколько вынести источник света вперёд от модели фонаря (м), чтобы он не засвечивал сам фонарь.");
        _hideGun = Bind("HideGun", true, "Прятать ствол автопушки у обеих турелей (остаётся тренога с корпусом).");
        _fogModelOffset = Bind("FogModelOffset", "0,0.25,0", "Где на FT1 стоит модель отпугивателя (x,y,z, м от верха турели).");
        _fogModelScale = Bind("FogModelScale", 1f, "Масштаб модели отпугивателя на FT1.");
        _fogLamp = Bind("FogLamp", 0.5f, "Яркость тусклой лампочки на FT1 (как у светящейся палки). 0 — без лампочки.");
    }

    private enum Mode { Fog, Light }

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
    }

    private static readonly Dictionary<IntPtr, Turret> _turrets = new();

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => _turrets.Clear();
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
            !(_turrets.TryGetValue(__instance.Pointer, out var t) && t.Mode == Mode.Fog);
    }

    // FT1 ничего не ищет: без сканирующего луча и без поворотов.
    [HarmonyPatch(typeof(SentryGunInstance), nameof(SentryGunInstance.WantToScan))]
    private static class Patch_Scan
    {
        private static bool Prefix(SentryGunInstance __instance, ref bool __result)
        {
            if (_turrets.TryGetValue(__instance.Pointer, out var t) && t.Mode == Mode.Fog) { __result = false; return false; }
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

    private static void Tick()
    {
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
                    t.Ready = t.Mode == Mode.Light ? SetupLight(t) : SetupFog(t);
                    if (!t.Ready && Time.time > t.GiveUpAt) { ModInfo.Log.LogWarning($"[TurretVariants] не удалось настроить турель ({t.Mode})"); t.Ready = true; }
                    else if (t.Ready) ModInfo.Log.LogInfo($"[TurretVariants] турель {t.Mode} настроена");
                }
                else
                {
                    UpdateScreen(t);
                    // Детали турели могут досоздаваться позже — несколько секунд повторяем «спрятать ствол».
                    if (Time.time - t.SpawnedAt < 8f && Time.time >= t.NextHide) { t.NextHide = Time.time + 0.5f; HideGun(t.Inst); }
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

        var go = new GameObject("GtfoPunk_Searchlight");
        go.transform.SetParent(align, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        try { AttachLamp(align); } catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] модель фонаря: {e.Message}"); }
        go.transform.localPosition = Vec(_lampOffset.Value, Vector3.zero) + Vector3.forward * _lightForward.Value; // свет перед стеклом фонаря, а не внутри модели
        HideGun(t.Inst);
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
        sphere.Range = _fogRange.Value;
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
        string text = t.Mode == Mode.Light ? "LT2" : "FT1";
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
        foreach (var mode in new[] { ItemMode.ThirdPerson, ItemMode.Pickup, ItemMode.Instance, ItemMode.FirstPerson })
        {
            if (perMode.Length <= (int)mode) continue;
            var dict = perMode[(int)mode];
            if (dict == null || !dict.ContainsKey(id)) continue;
            var list = dict[id];
            if (list != null && list.Count > 0 && list[0] != null) return list[0];
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
}
