using GameData;
using Il2CppInterop.Runtime.InteropTypes;
using AIGraph;
using LevelGeneration;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Frag grenade: a copy of the C-Foam Grenade (item 115 — the same models, throw and pickup) under its own item ID,
/// with its own name, that explodes instead of foaming (damage, fire and smoke of a mine, shake, noise).
/// It's a separate item, so it never stacks with glow sticks or foam grenades. In consumable boxes and lockers.
/// The grenade launcher sentry (TGL3) throws the game's unreleased grenade (item 136, cloned as 4250) — synced
/// like a player throw, exploding on contact and weaker.
/// Everyone needs it.
/// </summary>
public class Grenades : ModuleBase
{
    public override string Id => "Grenades";
    public override string Description => "Frag grenade (a C-Foam Grenade that explodes) among level consumables; grenades for the TGL3 sentry. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    /// <summary>The game's unreleased grenade (GrenadeBase script) — only the base of the sentry grenade now.</summary>
    public const uint GrenadeItemId = 136;
    /// <summary>Clone of item 136 thrown only by the TGL3 sentry — the item ID itself tells every client it's a sentry grenade.</summary>
    public const uint TurretGrenadeItemId = 4250;
    /// <summary>The frag grenade players carry: a clone of the C-Foam Grenade (never reuse or change this ID).</summary>
    public const uint FragItemId = 4252;
    /// <summary>Item whose look the grenade borrows (C-Foam Grenade).</summary>
    private const uint LookItemId = 115;
    /// <summary>Mark in the item data: this grenade was thrown by a sentry (visible to all players).</summary>
    private const float TurretMark = -77f;

    private static ConfigEntry<bool> _inLevels, _hitEnemies, _blastFx;
    private static ConfigEntry<float> _proximity, _noiseRadius, _shake;
    private static ConfigEntry<int> _stack;
    private static ConfigEntry<float> _weight, _turretRadius, _turretDamage, _turretFuse, _handRadius, _handDamage;
    private static ConfigEntry<string> _nameEn, _nameRu;

    protected override void OnBindConfig()
    {
        _nameEn = Bind("NameEn", "Frag Grenade", "Grenade name (English).");
        _nameRu = Bind("NameRu", "Граната", "Grenade name (Russian).");
        _inLevels = Bind("InLevels", true, "Grenades show up in levels among consumables (boxes, lockers).");
        _weight = Bind("SpawnWeight", 0.6f, "How often a grenade shows up: share of the average consumable weight in the level distribution (1 — like a regular consumable).");
        _stack = Bind("Stack", 2, "How many grenades (and C-Foam Grenades) a player can carry; a level pickup holds 1. 0 — as in the game.");
        _handRadius = BindMigrated(Id, "BlastRadius", 8f, "Grenade blast radius (m).", (Id, "HandBlastRadius", "8"));
        _handDamage = BindMigrated(Id, "BlastDamage", 120f, "Grenade damage at the blast center (falls off towards the edge; hurts players too).", (Id, "HandBlastDamage", "120"));
        _blastFx = Bind("BlastEffect", true, "Explosions look and sound like a mine going off (fire, smoke, light, sound).");
        _shake = Bind("BlastShake", 1f, "Camera shake from a grenade explosion (0 — none; sentry grenades shake half as much).");
        _noiseRadius = Bind("BlastNoiseRadius", 30f, "A grenade explosion wakes and alerts enemies within this radius (m; sentry grenades — half). 0 — no extra noise.");
        _turretRadius = Bind("TurretRadius", 3.5f, "Blast radius of the grenade launcher sentry's grenades (m).");
        _turretDamage = Bind("TurretDamage", 40f, "Sentry grenade damage at the blast center.");
        _turretFuse = Bind("TurretFuse", 0f, "Sentry grenade fuse after impact (s). 0 — on contact.");
        _proximity = Bind("TurretProximity", 0.8f, "Sentry grenades explode when they fly within this distance of an enemy (m). 0 — off.");
        _hitEnemies = Bind("BounceOffEnemies", true, "Sentry grenades hit enemies instead of flying through them.");
        ConfigMigration.DropOrphans(Config, Id); // HandModels, HandDelay, DiagnosticDump (0.9.x)
    }

    protected override void OnEnable()
    {
        GearFactory.OnInject += Inject;
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _pending.Clear(); _calib.Clear(); _armed.Clear(); _turretGrenades.Clear(); _exploded.Clear(); _live.Clear(); _frags.Clear(); _boomPool = null; _flashPool = null; };
    }

    // ---------- data: name and distribution ----------

    private static void Inject()
    {
        var foam = GameDataBlockBase<ItemDataBlock>.GetBlock(LookItemId);
        if (foam == null) { ModInfo.Log.LogWarning("[Grenades] C-Foam Grenade (115) is not in the game data — no grenade"); return; }
        string name = Lang.T(_nameEn.Value, _nameRu.Value);

        // The frag grenade: the C-Foam Grenade under its own ID (models, throw, pickup — all the foam grenade's).
        if (!GameDataBlockBase<ItemDataBlock>.HasBlock(FragItemId))
        {
            var frag = GearFactory.CloneBlock(foam);
            frag.persistentID = FragItemId;
            frag.name = "GtfoPunk_FragGrenade";
            GameDataBlockBase<ItemDataBlock>.AddBlock(frag);
        }
        var item = GameDataBlockBase<ItemDataBlock>.GetBlock(FragItemId);
        item.publicName = name;
        try { item.LocalizedName = GearFactory.Text(name); } catch { }
        try { item.terminalItemShortName = "GRENADE"; item.terminalItemLongName = GearFactory.Text(name); } catch { }
        _named = name;
        ModInfo.Log.LogInfo($"[Grenades] '{name}' ({FragItemId}): a copy of the C-Foam Grenade, models: first person {item.FirstPersonPrefabs?.Count ?? 0}, pickup {item.PickupPrefabs?.Count ?? 0}, thrown {item.InstancePrefabs?.Count ?? 0}");

        // Sentry grenade: the game's unreleased grenade 136 under its own fixed ID (prefabs load for it like for any item).
        var g136 = GameDataBlockBase<ItemDataBlock>.GetBlock(GrenadeItemId);
        if (g136 != null && g136.InstancePrefabs != null && g136.InstancePrefabs.Count > 0 && !GameDataBlockBase<ItemDataBlock>.HasBlock(TurretGrenadeItemId))
        {
            var tg = GearFactory.CloneBlock(g136);
            tg.persistentID = TurretGrenadeItemId;
            tg.name = "GtfoPunk_TurretGrenade";
            tg.publicName = Lang.T("Sentry Grenade", "Граната турели");
            try { tg.LocalizedName = GearFactory.Text(tg.publicName); } catch { }
            tg.registerInTerminalSystem = false;
            GameDataBlockBase<ItemDataBlock>.AddBlock(tg);
        }

        // Carried like C-Foam Grenades: one per pickup, a few in the pocket.
        if (_stack.Value > 0)
            foreach (var b in new[] { item, foam })
            {
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
            try { Reskin(TurretGrenadeItemId); } catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] look: {e.Message}"); }
        }
    }

    /// <summary>
    /// The sentry grenade (a clone of item 136) ships with glow stick models: hide them and put a copy of the C-Foam
    /// Grenade model in their place. Prefabs are changed once, so every spawned copy gets it.
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
                // The throwing script moves one transform through the swing and the release — that's where the grenade belongs.
                Transform thrown = null;
                try { thrown = dst.GetComponentInChildren<ThrowingWeapon>(true)?.m_movingThrowObj; } catch { }
                var mount = inHand ? (thrown ?? mover) : dst.transform;
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
                if (inHand) ModInfo.Log.LogInfo($"[Grenades] item {id} {(ItemMode)m} '{dst.name}': look on '{PathOf(mount, dst.transform)}'{(thrown != null ? " (throw object)" : "")} ({main.GetIl2CppType().Name} '{main.name}', scale {look.transform.localScale.x:0.###}, renderers {rs.Length})");
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
        bool ok = ItemSpawnManager.ItemHasMode(FragItemId, ItemMode.Pickup) && ItemSpawnManager.ItemHasMode(FragItemId, ItemMode.Instance)
                  && ItemSpawnManager.ItemHasMode(FragItemId, ItemMode.FirstPerson);
        if (!ok) { ModInfo.Log.LogWarning($"[Grenades] not all grenade ({FragItemId}) models loaded — not adding it to level distributions"); return; }
        _distributed = true;
        string name = _named;
        int lists = 0;
        foreach (var d in GameDataBlockBase<ConsumableDistributionDataBlock>.GetAllBlocks())
        {
            if (d?.SpawnData == null || d.SpawnData.Count == 0) continue;
            bool has = false; float sum = 0f; int n = 0;
            foreach (var s in d.SpawnData) { if (s == null) continue; if (s.ItemID == FragItemId) has = true; sum += s.Weight; n++; }
            if (has || n == 0) continue;
            var g = new ConsumableSpawnData { ItemID = FragItemId, Weight = Math.Max(0.01f, sum / n * _weight.Value) };
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

    // ---------- the frag grenade: a foam grenade that explodes ----------

    private static bool IsFrag(Item i) { try { return i != null && i.ItemDataBlock != null && i.ItemDataBlock.persistentID == FragItemId; } catch { return false; } }

    /// <summary>
    /// The foam grenade's own detonation (after its fuse): no glue globs for ours — a blast instead. The rest of the
    /// sequence runs as usual (the grenade disappears like a foam grenade does). Runs on every machine (the throw is
    /// synced); the thrower's machine deals the damage, like with bullets.
    /// </summary>
    [HarmonyPatch(typeof(GlueGrenadeInstance), nameof(GlueGrenadeInstance.ExplosionSequence))]
    private static class Patch_FragDetonate
    {
        private static void Prefix(GlueGrenadeInstance __instance, ref int projectiles, Vector3 pos)
        {
            if (!IsFrag(__instance)) return;
            projectiles = 0;
            try
            {
                if (__instance.m_graphics != null) foreach (var r in __instance.m_graphics) if (r != null) r.enabled = false;
                if (!_exploded.Add(__instance.Pointer)) return;
                PlayerAgent owner = null;
                try { owner = __instance.Owner; } catch { }
                bool dealDamage = owner != null ? owner.IsLocallyOwned : SNetwork.SNet.IsMaster;
                if (dealDamage)
                    DamageUtil.DoExplosionDamage(pos, _handRadius.Value, _handDamage.Value, LayerManager.MASK_EXPLOSION_TARGETS, LayerManager.MASK_EXPLOSION_BLOCKERS, true, 1000f);
                Bang(__instance.Pointer, pos, _handRadius.Value, (int)_handDamage.Value, owner, dealDamage ? "fuse, damage dealt here" : "fuse");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] frag grenade: {e.Message}"); }
        }
    }

    private class Frag { public GlueGrenadeInstance G; public IntPtr Key; public Vector3 Pos; public PlayerAgent Owner; }
    private static readonly List<Frag> _frags = new();

    /// <summary>Thrown frag grenades: no glue globs, and watched in case the detonation call is inlined by the game.</summary>
    [HarmonyPatch(typeof(GlueGrenadeInstance), nameof(GlueGrenadeInstance.Start))]
    private static class Patch_FragStart
    {
        private static void Postfix(GlueGrenadeInstance __instance)
        {
            if (!IsFrag(__instance)) return;
            try
            {
                __instance.m_projCount = 0;
                PlayerAgent owner = null;
                try { owner = __instance.Owner; } catch { }
                _frags.Add(new Frag { G = __instance, Key = __instance.Pointer, Pos = __instance.transform.position, Owner = owner });
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] frag grenade: {e.Message}"); }
        }
    }

    /// <summary>A frag grenade that went off (or vanished) without our detonation hook firing — explode it here.</summary>
    private static void FragTick()
    {
        for (int i = _frags.Count - 1; i >= 0; i--)
        {
            var f = _frags[i];
            bool gone = true, fired = false;
            try
            {
                gone = f.G == null || f.G.gameObject == null || !f.G.gameObject.activeInHierarchy;
                if (!gone) { f.Pos = f.G.transform.position; fired = f.G.m_exploded; }
            }
            catch { }
            if (!gone && !fired) continue;
            _frags.RemoveAt(i);
            // Done with this grenade either way: forget its pointer (a new object may get the same address later).
            if (!_exploded.Add(f.Key)) { _exploded.Remove(f.Key); continue; } // the detonation hook did it
            _exploded.Remove(f.Key);
            try
            {
                bool dealDamage = f.Owner != null ? f.Owner.IsLocallyOwned : SNetwork.SNet.IsMaster;
                if (dealDamage)
                    DamageUtil.DoExplosionDamage(f.Pos, _handRadius.Value, _handDamage.Value, LayerManager.MASK_EXPLOSION_TARGETS, LayerManager.MASK_EXPLOSION_BLOCKERS, true, 1000f);
                Bang(f.Key, f.Pos, _handRadius.Value, (int)_handDamage.Value, f.Owner, gone ? "gone" : "exploded");
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[Grenades] frag grenade: {e.Message}"); }
        }
    }

    // ---------- behaviour of thrown sentry grenades ----------

    private static readonly List<GrenadeBase> _pending = new();

    [HarmonyPatch(typeof(GrenadeBase), nameof(GrenadeBase.Start))]
    private static class Patch_Start
    {
        private static void Postfix(GrenadeBase __instance) { if (__instance != null) _pending.Add(__instance); }
    }

    private static void Tick()
    {
        if (_frags.Count > 0) FragTick();
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
            // Gone for good: forget its pointer (a new grenade may get the same address — the launcher fires a lot).
            _exploded.Remove(l.Key); _turretGrenades.Remove(l.Key);
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
}
