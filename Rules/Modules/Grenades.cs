using GameData;
using Il2CppInterop.Runtime.InteropTypes;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Grenade. The game has an unreleased item 136 (CONSUMABLE_Grenade) with a ready-made GrenadeBase script
/// (radius, damage, delay, explosion). This module:
///  - names it and adds it to the level consumable distribution (boxes/lockers), like foam and mines;
///  - gives sentries a way to "throw" a grenade (synced, like a player throw) — for the grenade launcher sentry;
///  - sentry grenades explode on contact and are weaker (radius/damage from config).
/// Everyone needs it.
/// </summary>
public class Grenades : ModuleBase
{
    public override string Id => "Grenades";
    public override string Description => "Grenade: the game's unreleased item 136 — in consumable boxes and for the grenade launcher sentry. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    public const uint GrenadeItemId = 136;
    /// <summary>Clone of item 136 thrown only by the TGL3 sentry — the item ID itself tells every client it's a sentry grenade.</summary>
    public const uint TurretGrenadeItemId = 4250;
    /// <summary>Item whose look the grenade borrows (C-Foam Grenade).</summary>
    private const uint LookItemId = 115;
    /// <summary>Mark in the item data: this grenade was thrown by a sentry (visible to all players).</summary>
    private const float TurretMark = -77f;

    private static ConfigEntry<bool> _inLevels, _diag, _hitEnemies, _blastFx;
    private static ConfigEntry<float> _proximity, _noiseRadius, _shake;
    private static ConfigEntry<int> _stack;
    private static ConfigEntry<float> _weight, _turretRadius, _turretDamage, _turretFuse, _handRadius, _handDamage, _handDelay;
    private static ConfigEntry<string> _nameEn, _nameRu;

    protected override void OnBindConfig()
    {
        _inLevels = Bind("InLevels", true, "Grenades show up in levels among consumables (boxes, lockers).");
        _weight = Bind("SpawnWeight", 0.6f, "How often a grenade shows up: fraction of the average consumable weight in the level distribution (1 — like a regular consumable).");
        _nameEn = Bind("NameEn", "Frag Grenade", "Grenade name (English).");
        _nameRu = Bind("NameRu", "Граната", "Grenade name (Russian).");
        _stack = Bind("Stack", 2, "How many hand grenades (and C-Foam Grenades) a player can carry. Level pickups hold 1. 0 — as in the game.");
        _handRadius = BindMigrated(Id, "HandBlastRadius", 8f, "Hand grenade blast radius (m). 0 — as designed in the game.", (Id, "HandRadius", "0"));
        _handDamage = BindMigrated(Id, "HandBlastDamage", 120f, "Hand grenade damage at the center. 0 — as designed in the game.", (Id, "HandDamage", "0"));
        _blastFx = Bind("BlastEffect", true, "Explosions look and sound like a mine going off (fire, smoke, light, sound), not a silent pop.");
        _shake = Bind("BlastShake", 1f, "Camera shake from a hand grenade explosion (0 — none; sentry grenades shake half as much).");
        _noiseRadius = Bind("BlastNoiseRadius", 30f, "A hand grenade explosion wakes and alerts enemies within this radius (m; sentry grenades — half). 0 — no extra noise.");
        _handDelay = Bind("HandDelay", -1f, "Hand grenade fuse after impact (s). -1 — as designed in the game.");
        _turretRadius = Bind("TurretRadius", 3.5f, "Blast radius of the grenade launcher sentry's grenades (m; a mine's is bigger).");
        _turretDamage = Bind("TurretDamage", 40f, "Sentry grenade damage at the blast center.");
        _turretFuse = Bind("TurretFuse", 0f, "Sentry grenade fuse after impact (s). 0 — on contact.");
        _proximity = Bind("TurretProximity", 0.8f, "Sentry grenades explode when they fly within this distance of an enemy (m). 0 — off.");
        _hitEnemies = Bind("BounceOffEnemies", true, "Grenades hit enemies instead of flying through them.");
        _diag = Bind("DiagnosticDump", false, "Write grenade model details to the log at the first drop-in (diagnostics; may cause a short hitch).");
        ConfigMigration.DropOrphans(Config, Id);
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _pending.Clear(); _calib.Clear(); _armed.Clear(); _turretGrenades.Clear(); _exploded.Clear(); _live.Clear(); _boomPool = null; _flashPool = null; _dumped = false; };
    }

    // ---------- data: name and distribution ----------

    private static void Inject()
    {
        var item = GameDataBlockBase<ItemDataBlock>.GetBlock(GrenadeItemId);
        if (item == null) { ModInfo.Log.LogWarning("[Grenades] item 136 is not in the game data — no grenade"); return; }
        int prefabs = (item.FirstPersonPrefabs?.Count ?? 0) + (item.InstancePrefabs?.Count ?? 0) + (item.PickupPrefabs?.Count ?? 0);
        ModInfo.Log.LogInfo($"[Grenades] item 136 '{item.publicName}', slot {item.inventorySlot}, models: first person {item.FirstPersonPrefabs?.Count ?? 0}, pickup {item.PickupPrefabs?.Count ?? 0}, thrown {item.InstancePrefabs?.Count ?? 0}, charges {item.ConsumableAmmoMin}–{item.ConsumableAmmoMax}");
        if (item.InstancePrefabs == null || item.InstancePrefabs.Count == 0) { ModInfo.Log.LogWarning("[Grenades] item 136 has no thrown model — leaving it alone"); return; }

        string name = Lang.T(_nameEn.Value, _nameRu.Value);
        item.publicName = name;
        try { item.LocalizedName = GearFactory.Text(name); } catch { }
        try { item.terminalItemShortName = "GRENADE"; item.terminalItemLongName = GearFactory.Text(name); } catch { }

        _named = name;

        // Sentry grenade: same item under its own fixed ID (prefabs load for it like for any item).
        if (!GameDataBlockBase<ItemDataBlock>.HasBlock(TurretGrenadeItemId))
        {
            var tg = GearFactory.CloneBlock(item);
            tg.persistentID = TurretGrenadeItemId;
            tg.name = "GtfoPunk_TurretGrenade";
            tg.publicName = Lang.T("Sentry Grenade", "Граната турели");
            try { tg.LocalizedName = GearFactory.Text(tg.publicName); } catch { }
            tg.registerInTerminalSystem = false;
            GameDataBlockBase<ItemDataBlock>.AddBlock(tg);
        }
        // Inventory icon of the C-Foam Grenade.
        var look = GameDataBlockBase<ItemDataBlock>.GetBlock(LookItemId);
        if (look != null && !string.IsNullOrEmpty(look.HUDIcon)) item.HUDIcon = look.HUDIcon;

        // Carried like C-Foam Grenades (one per pickup, a few in the pocket), not like a pack of glow sticks.
        if (_stack.Value > 0)
            foreach (var b in new[] { item, look })
            {
                if (b == null) continue;
                b.ConsumableAmmoMin = 1;
                b.ConsumableAmmoMax = _stack.Value;
                ModInfo.Log.LogInfo($"[Grenades] {b.publicName} ({b.persistentID}): carry up to {_stack.Value}");
            }
    }

    private static string _named;
    private static bool _distributed;

    // Only touch the distribution once item models are loaded and the grenade has them —
    // otherwise a broken item in the distribution could break level generation.
    [HarmonyPatch(typeof(ItemSpawnManager), nameof(ItemSpawnManager.SetupItemPrefabs))]
    private static class Patch_Prefabs
    {
        private static void Postfix()
        {
            try { Distribute(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] distribution: {e.Message}"); }
            try { Reskin(GrenadeItemId); Reskin(TurretGrenadeItemId); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] look: {e.Message}"); }
        }
    }

    /// <summary>
    /// Item 136 ships with glow stick models. In every view (hand, third person, pickup, thrown) hide the glow stick
    /// meshes and put a copy of the C-Foam Grenade model in their place. Prefabs are changed once, so every spawned copy gets it.
    /// </summary>
    private static void Reskin(uint id)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return;
        int done = 0;
        for (int m = 0; m < perMode.Length; m++)
        {
            var dict = perMode[m];
            if (dict == null || !dict.ContainsKey(id) || !dict.ContainsKey(LookItemId)) continue;
            GameObject src = null;
            foreach (var cand in dict[LookItemId])
                if (cand != null && cand.GetComponentsInChildren<Renderer>(true).Length > 0) { src = cand; break; }
            if (src == null) continue;
            foreach (var dst in dict[id])
            {
                // Items 136 and 4250 share the same prefabs: skip a model already done (the look may sit deep inside).
                if (dst == null || dst.GetComponentsInChildren<Transform>(true).Any(t => t.name == "GtfoPunk_Look")) continue;
                var rs = dst.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                foreach (var r in rs) { r.enabled = false; r.forceRenderingOff = true; }
                // In hand (first / third person) the glow stick mesh moves with the throwing animation, the prefab root
                // doesn't — put the look on the mesh so it leaves the hand when the throw does.
                bool inHand = m == (int)ItemMode.FirstPerson || m == (int)ItemMode.ThirdPerson;
                var main = MainRenderer(rs);
                // A skinned mesh stays put and its bones move — mount on the bone then.
                var skinned = main.TryCast<SkinnedMeshRenderer>();
                var mover = skinned != null ? (skinned.rootBone ?? (skinned.bones != null && skinned.bones.Length > 0 ? skinned.bones[0] : null)) ?? main.transform : main.transform;
                var mount = inHand ? mover : dst.transform;
                var look = TurretVariants.CopyModel(src, mount, "GtfoPunk_Look");
                if (look == null) continue;
                // Same layer as the stick it replaces (first person items are drawn by their own camera).
                foreach (var tr in look.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = main.gameObject.layer;
                if (mount != dst.transform)
                {
                    // Keep the size it had on the root (the mesh node may be scaled).
                    var want = dst.transform.lossyScale; var have = mount.lossyScale;
                    var k = new Vector3(want.x / Math.Max(1e-4f, have.x), want.y / Math.Max(1e-4f, have.y), want.z / Math.Max(1e-4f, have.z));
                    if (k.x < 0.01f || k.x > 100f || float.IsNaN(k.x)) k = Vector3.one;
                    look.transform.localScale = k;
                }
                if (inHand) ModInfo.Log.LogInfo($"[Grenades] item {id} {(ItemMode)m} '{dst.name}': look on '{PathOf(mount, dst.transform)}' ({main.GetIl2CppType().Name} '{main.name}', scale {look.transform.localScale.x:0.###}, renderers {rs.Length})");
                done++;
            }
        }
        if (done > 0) ModInfo.Log.LogInfo($"[Grenades] item {id}: C-Foam Grenade look applied to {done} models");
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Add(x.name);
        parts.Reverse();
        return parts.Count == 0 ? "(root)" : string.Join("/", parts);
    }

    /// <summary>The biggest mesh of the model (the glow stick itself, not small extras).</summary>
    private static Renderer MainRenderer(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Renderer> rs)
    {
        Renderer best = rs[0];
        float size = -1f;
        foreach (var r in rs)
        {
            float v = r.bounds.size.sqrMagnitude;
            if (v > size) { size = v; best = r; }
        }
        return best;
    }

    private static void Distribute()
    {
        if (_distributed || _named == null || !_inLevels.Value) return;
        bool ok = ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.Pickup) && ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.Instance)
                  && ItemSpawnManager.ItemHasMode(GrenadeItemId, ItemMode.FirstPerson);
        if (!ok) { ModInfo.Log.LogWarning("[Grenades] not all grenade (136) models loaded — not adding it to level distributions"); return; }
        _distributed = true;
        string name = _named;
        int lists = 0;
        foreach (var d in GameDataBlockBase<ConsumableDistributionDataBlock>.GetAllBlocks())
        {
            if (d?.SpawnData == null || d.SpawnData.Count == 0) continue;
            bool has = false; float sum = 0f; int n = 0;
            foreach (var s in d.SpawnData) { if (s == null) continue; if (s.ItemID == GrenadeItemId) has = true; sum += s.Weight; n++; }
            if (has || n == 0) continue;
            var g = new ConsumableSpawnData { ItemID = GrenadeItemId, Weight = Math.Max(0.01f, sum / n * _weight.Value) };
            d.SpawnData.Add(g);
            lists++;
        }
        ModInfo.Log.LogInfo($"[Grenades] grenade '{name}' added to consumable distributions: {lists}");
    }

    // ---------- throw (for the sentry) ----------

    private static float _forceFactor = 1f;
    private static readonly List<(float time, Vector3 vel)> _calib = new();

    /// <summary>Throws a grenade synced for everyone (call on the host). Returns false if the item isn't available.</summary>
    public static bool Throw(PlayerAgent owner, Vector3 from, Vector3 velocity, AIG_CourseNode node)
    {
        uint id = ItemSpawnManager.ItemHasMode(TurretGrenadeItemId, ItemMode.Instance) ? TurretGrenadeItemId : GrenadeItemId;
        if (!ItemSpawnManager.ItemHasMode(id, ItemMode.Instance)) return false;
        var data = new pItemData { itemID_gearCRC = id };
        data.custom = new pItemData_Custom { ammo = TurretMark };
        var rot = velocity.sqrMagnitude > 0.01f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
        ItemReplicationManager.ThrowItem(data, null, ItemMode.Instance, from, rot, velocity * _forceFactor, from, node, owner);
        _calib.Add((Time.time, velocity));
        return true;
    }

    // ---------- behaviour of thrown grenades ----------

    private static readonly List<GrenadeBase> _pending = new();

    [HarmonyPatch(typeof(GrenadeBase), nameof(GrenadeBase.Start))]
    private static class Patch_Start
    {
        private static void Postfix(GrenadeBase __instance) { if (__instance != null) _pending.Add(__instance); }
    }

    private static bool _dumped;

    private static void Tick()
    {
        if (_diag.Value && !_dumped && GameUtil.InLevel) { _dumped = true; try { Dump(); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] {e.Message}"); } }
        if (_armed.Count > 0) ProximityTick();
        if (_live.Count > 0) WatchTick();
        if (_pending.Count == 0) return;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var g = _pending[i];
            _pending.RemoveAt(i);
            if (g == null) continue;
            try { Configure(g); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] {e.Message}"); }
        }
    }

    private static void Configure(GrenadeBase g)
    {
        bool turret = false;
        try { turret = g.ItemDataBlock != null && g.ItemDataBlock.persistentID == TurretGrenadeItemId; } catch { }
        if (!turret) try { turret = Math.Abs(g.Get_pItemData().custom.ammo - TurretMark) < 0.01f; } catch { }
        // Item 136 was never released: check its collision / explosion layers and fill in what's missing.
        int coll = g.m_projCollideMask, target = g.m_explosionTargetMask, block = g.m_explosionBlockMask;
        if (g.m_explosionTargetMask == 0) g.m_explosionTargetMask = LayerManager.MASK_EXPLOSION_TARGETS;
        if (g.m_explosionBlockMask == 0) g.m_explosionBlockMask = LayerManager.MASK_EXPLOSION_BLOCKERS;
        if (g.m_projCollideMask == 0) g.m_projCollideMask = LayerManager.MASK_WORLD;
        if (_hitEnemies.Value) g.m_projCollideMask |= LayerManager.MASK_ENEMY_DAMAGABLE;
        if (_maskLogs++ < 4)
            ModInfo.Log.LogInfo($"[Grenades] layers: collide {coll:X} → {g.m_projCollideMask:X}, targets {target:X} → {g.m_explosionTargetMask:X}, blockers {block:X} → {g.m_explosionBlockMask:X} (explosion targets {LayerManager.MASK_EXPLOSION_TARGETS:X}, enemies {LayerManager.MASK_ENEMY_DAMAGABLE:X})");
        if (turret)
        {
            _turretGrenades.Add(g.Pointer);
            if (_proximity.Value > 0f) _armed.Add((g, Time.time + 0.15f));
            g.m_grenadeExplosionRadius = _turretRadius.Value;
            g.m_grenadeMaxDamage = (int)_turretDamage.Value;
            g.m_grenadeExplosionDelay = Math.Max(0f, _turretFuse.Value);
            Calibrate(g);
        }
        else
        {
            if (_handRadius.Value > 0f) g.m_grenadeExplosionRadius = _handRadius.Value;
            if (_handDamage.Value > 0f) g.m_grenadeMaxDamage = (int)_handDamage.Value;
            if (_handDelay.Value >= 0f) g.m_grenadeExplosionDelay = _handDelay.Value;
        }
        ModInfo.Log.LogInfo($"[Grenades] {(turret ? "sentry grenade" : "grenade")}: radius {g.m_grenadeExplosionRadius:0.#} m, damage {g.m_grenadeMaxDamage}, fuse {g.m_grenadeExplosionDelay:0.##} s");
        _live.Add(new Live { G = g, Key = g.Pointer, Pos = g.transform.position, Radius = g.m_grenadeExplosionRadius, Damage = g.m_grenadeMaxDamage, Owner = SafeOwner(g) });
    }

    private static int _maskLogs;
    private static readonly List<(GrenadeBase g, float armAt)> _armed = new();

    /// <summary>Proximity fuse for sentry grenades: physics may not let them touch enemies, so check distance ourselves.</summary>
    private static void ProximityTick()
    {
        for (int i = _armed.Count - 1; i >= 0; i--)
        {
            var (g, armAt) = _armed[i];
            if (g == null || g.gameObject == null || !g.gameObject.activeInHierarchy) { _armed.RemoveAt(i); continue; }
            if (Time.time < armAt) continue;
            if (Physics.CheckSphere(g.transform.position, _proximity.Value, LayerManager.MASK_ENEMY_DAMAGABLE))
            {
                _armed.RemoveAt(i);
                try { g.GrenadeDelay(); ModInfo.Log.LogInfo("[Grenades] sentry grenade: proximity fuse"); }
                catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] proximity fuse: {e.Message}"); }
            }
        }
    }

    // ---------- the bang ----------

    private static readonly HashSet<IntPtr> _turretGrenades = new(), _exploded = new();
    private static bool _fxLogged;

    /// <summary>GrenadeBase.GrenadeDelay is the explosion itself (OnCollisionEnter schedules it after the fuse).</summary>
    [HarmonyPatch(typeof(GrenadeBase), nameof(GrenadeBase.GrenadeDelay))]
    private static class Patch_Explode
    {
        private static void Prefix(GrenadeBase __instance)
        {
            if (__instance == null || !_exploded.Add(__instance.Pointer)) return;
            try { Bang(__instance.Pointer, __instance.transform.position, __instance.m_grenadeExplosionRadius, __instance.m_grenadeMaxDamage, SafeOwner(__instance), "fuse"); }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] explosion effect: {e.Message}"); }
        }
    }

    private static PlayerAgent SafeOwner(GrenadeBase g) { try { return g.Owner; } catch { return null; } }

    private static void Bang(IntPtr key, Vector3 pos, float radius, int damage, PlayerAgent owner, string how)
    {
        bool turret = _turretGrenades.Contains(key);
        float scale = turret ? 0.5f : 1f;
        ModInfo.Log.LogInfo($"[Grenades] {(turret ? "sentry grenade" : "grenade")} exploded at {pos} ({how}), radius {radius:0.#} m, damage {damage}");
        if (_blastFx.Value)
        {
            try { CellSound.Post(AK.EVENTS.STICKYMINEEXPLODE, pos); } catch { }
            bool fx = false;
            try { fx = PlayMineEffect(pos); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] effect: {e.Message}"); }
            if (!fx) Flash(pos, radius);
        }
        if (_shake.Value > 0f) Shake(pos, radius, _shake.Value * scale);
        if (_noiseRadius.Value > 0f && SNetwork.SNet.IsMaster) Noise(owner, pos, _noiseRadius.Value * scale);
    }

    // The hand grenade's explosion doesn't always go through GrenadeDelay — also watch the thrown grenades and
    // treat one that disappears (and wasn't seen exploding) as exploded where it was last.
    private class Live { public GrenadeBase G; public IntPtr Key; public Vector3 Pos; public float Radius; public int Damage; public PlayerAgent Owner; }
    private static readonly List<Live> _live = new();

    private static void WatchTick()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var l = _live[i];
            bool gone = true;
            try { gone = l.G == null || l.G.gameObject == null || !l.G.gameObject.activeInHierarchy; } catch { }
            if (!gone) { try { l.Pos = l.G.transform.position; l.Radius = l.G.m_grenadeExplosionRadius; l.Damage = l.G.m_grenadeMaxDamage; } catch { } continue; }
            _live.RemoveAt(i);
            if (_exploded.Add(l.Key)) try { Bang(l.Key, l.Pos, l.Radius, l.Damage, l.Owner, "gone"); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] explosion effect: {e.Message}"); }
        }
    }

    /// <summary>The tripmine's explosion (fire, smoke) and its flash, from the game's effect pools.</summary>
    private static bool PlayMineEffect(Vector3 pos)
    {
        bool boom = PlayPooled(pos, "TripmineExplosion", ref _boomPool);
        PlayPooled(pos, "TripmineFlash", ref _flashPool);
        return boom;
    }

    private static FX_EffectSystem.FX_Pool _boomPool, _flashPool;
    private static bool _poolsLogged;

    /// <summary>Plays one effect from the pool whose prefab name contains the key (not the glue mine's); a fresh copy if the pool is empty.</summary>
    private static bool PlayPooled(Vector3 pos, string key, ref FX_EffectSystem.FX_Pool pool)
    {
        if (pool == null) pool = FindPool(key);
        if (pool == null) return false;
        var rot = Quaternion.LookRotation(Vector3.up);
        FX_EffectSystem.FX_EffectBase fx = null;
        try { fx = pool.AquireEffect(); } catch { }
        string how = "pool";
        if (fx == null && pool.m_prefab != null)
        {
            var go = UnityEngine.Object.Instantiate(pool.m_prefab, pos, rot);
            fx = go.GetComponentInChildren<FX_EffectSystem.FX_EffectBase>(true);
            UnityEngine.Object.Destroy(go, 10f);
            how = "copy";
            if (fx == null) return false;
        }
        if (fx == null) return false;
        fx.gameObject.SetActive(true);
        fx.Play(null, pos, rot);
        if (!_fxLogged || how == "copy" && _copyLogs++ < 2)
        {
            _fxLogged = true;
            ModInfo.Log.LogInfo($"[Grenades] explosion effect '{fx.gameObject.name}' ({how}, key {key})");
        }
        return true;
    }

    private static int _copyLogs;

    private static FX_EffectSystem.FX_Pool FindPool(string key)
    {
        FX_EffectSystem.FX_Pool pool = null;
        if (key == "TripmineExplosion") try { pool = MineDeployerInstance_Detonate_Explosive.s_explosionPool; } catch { }
        if (pool != null) return pool;
        var mgr = FX_EffectSystem.FX_Manager.Current;
        if (mgr?.m_pools == null) return null;
        var names = new List<string>();
        foreach (var p in mgr.m_pools)
        {
            if (p == null) continue;
            string n = p.m_prefab != null ? p.m_prefab.name : p.gameObject.name;
            names.Add(n);
            if (pool == null && n.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("glue", StringComparison.OrdinalIgnoreCase) < 0) pool = p;
        }
        if (pool == null && !_poolsLogged) { _poolsLogged = true; ModInfo.Log.LogInfo($"[Grenades] no '{key}' effect pool; pools: {string.Join(", ", names)}"); }
        return pool;
    }

    /// <summary>Fallback when no explosion effect is found: a short bright flash (a light GTFO actually draws).</summary>
    private static void Flash(Vector3 pos, float radius)
    {
        var go = new GameObject("GtfoPunk_GrenadeFlash");
        go.transform.position = pos + Vector3.up * 0.3f;
        TurretVariants.MakeLight(go, LightType.Point, Math.Max(4f, radius * 2.5f), 8f, new Color(1f, 0.7f, 0.35f));
        UnityEngine.Object.Destroy(go, 0.25f);
    }

    private static void Shake(Vector3 pos, float radius, float amount)
    {
        var go = new GameObject("GtfoPunk_GrenadeShake");
        go.transform.position = pos;
        var s = go.AddComponent<CameraShakeEffect>();
        s.Radius = Math.Max(6f, radius * 3f);
        s.Duration = 0.8f;
        s.Amplitude = amount;
        s.Frequency = 25f;
        s.directional = false;
        s.Play();
        UnityEngine.Object.Destroy(go, 2f);
    }

    /// <summary>"Good morning, Vietnam": everything within the radius wakes up and comes to look.</summary>
    private static void Noise(PlayerAgent owner, Vector3 pos, float radius)
    {
        owner ??= PlayerManager.GetLocalPlayerAgent();
        if (owner == null) return;
        if (!AIG_CourseNode.TryGetCourseNode(owner.DimensionIndex, pos, 3f, out var node)) node = owner.CourseNode;
        if (node == null) return;
        NoiseManager.MakeNoise(new NM_NoiseData
        {
            noiseMaker = ((Il2CppObjectBase)owner).Cast<INM_NoiseMaker>(),
            position = pos,
            node = node,
            radiusMin = radius * 0.5f,
            radiusMax = radius,
            yScale = 1f,
            type = NM_NoiseType.InstaDetect,
            includeToNeightbourAreas = true,
            raycastFirstNode = false,
        });
    }

    /// <summary>Throw force tuning: compare the velocity we wanted with what we got (on the host).</summary>
    private static void Calibrate(GrenadeBase g)
    {
        if (!SNetwork.SNet.IsMaster || _calib.Count == 0) return;
        var rb = g.GetComponent<Rigidbody>() ?? g.GetComponentInChildren<Rigidbody>();
        var (time, want) = _calib[0];
        _calib.RemoveAt(0);
        if (rb == null || Time.time - time > 1f) return;
        float got = rb.velocity.magnitude, need = want.magnitude;
        if (got < 0.5f || need < 0.5f) { ModInfo.Log.LogInfo($"[Grenades] grenade speed {got:0.0} m/s (wanted {need:0.0}), mass {rb.mass:0.##}"); return; }
        float k = need / got;
        _forceFactor = Mathf.Clamp(_forceFactor * Mathf.Lerp(1f, k, 0.7f), 0.01f, 100f);
        ModInfo.Log.LogInfo($"[Grenades] grenade speed {got:0.0} m/s (wanted {need:0.0}), mass {rb.mass:0.##} → force factor {_forceFactor:0.###}");
    }

    private static void Dump()
    {
        ModInfo.Log.LogInfo($"[Grenades] sentry grenade item {TurretGrenadeItemId}: thrown model loaded = {ItemSpawnManager.ItemHasMode(TurretGrenadeItemId, ItemMode.Instance)}");
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null) return;
        for (int m = 0; m < perMode.Length; m++)
        {
            var dict = perMode[m];
            if (dict == null || !dict.ContainsKey(GrenadeItemId)) continue;
            var list = dict[GrenadeItemId];
            for (int k = 0; list != null && k < list.Count; k++)
            {
                var go = list[k];
                if (go == null) continue;
                var comps = go.GetComponentsInChildren<Component>(true).Select(c => c == null ? "null" : c.GetIl2CppType().Name).GroupBy(n => n).Select(x => $"{x.Key}x{x.Count()}");
                ModInfo.Log.LogInfo($"[Grenades] model 136 mode {(ItemMode)m} [{k}] '{go.name}': {string.Join(", ", comps)}");
            }
        }
    }
}
