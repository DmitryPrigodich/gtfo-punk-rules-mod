using GameData;
using Gear;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Models on the sentries: copies of item models (lamp, repeller, foam tanks) and of gear parts (bio-tracker, foam
/// launcher, mine deployer) mounted in the barrel cradle. Gear part models are only in memory while some gear uses
/// them, so the first time we see one we keep our own hidden copy (template) for the rest of the session.
/// </summary>
public partial class TurretVariants
{
    // ---------- item models ----------

    private static readonly ItemMode[] PickupFirst = { ItemMode.Pickup, ItemMode.ThirdPerson, ItemMode.Instance, ItemMode.FirstPerson };
    private static readonly ItemMode[] InstanceFirst = { ItemMode.Instance, ItemMode.Pickup, ItemMode.ThirdPerson, ItemMode.FirstPerson };

    /// <summary>The first loaded model variant of the item that actually has meshes, trying the modes in order.</summary>
    private static GameObject ItemModel(uint id, ItemMode[] order = null)
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null || id == 0) return null;
        foreach (var mode in order ?? PickupFirst)
        {
            if (perMode.Length <= (int)mode) continue;
            var dict = perMode[(int)mode];
            if (dict == null || !dict.ContainsKey(id)) continue;
            var list = dict[id];
            if (list == null) continue;
            for (int k = 0; k < list.Count; k++)
                if (list[k] != null && list[k].GetComponentsInChildren<Renderer>(true).Length > 0) return list[k];
        }
        return null;
    }

    private static readonly Dictionary<string, uint> _itemIds = new();

    /// <summary>Item ID by (part of) its name, letters and digits only, case-insensitive. 0 if not found.</summary>
    private static uint FindItemId(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        if (_itemIds.TryGetValue(name, out var cached)) return cached;
        static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        string want = Norm(name);
        uint found = 0;
        foreach (var b in GameDataBlockBase<ItemDataBlock>.GetAllBlocks())
        {
            if (b == null) continue;
            if (Norm(b.publicName).Contains(want) || Norm(b.name).Contains(want)) { found = b.persistentID; break; }
        }
        if (found == 0) ModInfo.Log.LogWarning($"[TurretVariants] item '{name}' not found");
        _itemIds[name] = found;
        return found;
    }

    /// <summary>Mounts a copy of an item's model under the parent. Null if the model isn't loaded.</summary>
    private static GameObject AttachItemModel(Transform parent, uint id, string name, ItemMode[] order = null)
    {
        var prefab = ItemModel(id, order);
        if (prefab == null) { if (id != 0) ModInfo.Log.LogWarning($"[TurretVariants] model of item {id} not loaded"); return null; }
        return CopyModel(prefab, parent, name);
    }

    // ---------- gear part models (kept as templates) ----------

    private static GameObject _templateRoot;
    private static readonly Dictionary<string, GameObject> _templates = new();

    private static Transform TemplateRoot
    {
        get
        {
            if (_templateRoot == null)
            {
                _templateRoot = new GameObject("GtfoPunk_ModelTemplates");
                _templateRoot.SetActive(false); // templates are never visible
                UnityEngine.Object.DontDestroyOnLoad(_templateRoot);
            }
            return _templateRoot.transform;
        }
    }

    /// <summary>Parts with subtypes (Delivery/Payload/Targeting) have a model per subtype: the requested one or the first with a model.</summary>
    private static GearPartGeneralData Pick<T>(Il2CppSystem.Collections.Generic.List<T> list, Func<T, (int type, GearPartGeneralData data)> get, int sub)
    {
        if (list == null) return null;
        GearPartGeneralData first = null;
        foreach (var item in list)
        {
            var (type, data) = get(item);
            if (data == null || string.IsNullOrEmpty(data.Model)) continue;
            if (sub >= 0 && type == sub) return data;
            first ??= data;
        }
        return first;
    }

    /// <summary>Model file name of a gear part (type = component number from GearList, e.g. 33 Delivery).</summary>
    private static string PartModelName(int type, uint id, int sub)
    {
        GearPartGeneralData general = type switch
        {
            12 => GameDataBlockBase<GearFrontPartDataBlock>.GetBlock(id)?.General,
            16 => GameDataBlockBase<GearReceiverPartDataBlock>.GetBlock(id)?.General,
            27 => GameDataBlockBase<GearToolMainPartDataBlock>.GetBlock(id)?.General,
            30 => GameDataBlockBase<GearToolGripPartDataBlock>.GetBlock(id)?.General,
            33 => Pick(GameDataBlockBase<GearToolDeliveryPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            37 => Pick(GameDataBlockBase<GearToolPayloadPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            40 => Pick(GameDataBlockBase<GearToolTargetingPartDataBlock>.GetBlock(id)?.GeneralDatas, d => ((int)d.Type, d.Data), sub),
            42 => GameDataBlockBase<GearToolScreenPartDataBlock>.GetBlock(id)?.General,
            _ => null,
        };
        string model = general?.Model;
        return string.IsNullOrEmpty(model) ? null : System.IO.Path.GetFileNameWithoutExtension(model);
    }

    /// <summary>"type=ID[/subtype], …" → model names.</summary>
    private static IEnumerable<(string Spec, string Name)> ParseParts(string spec)
    {
        foreach (var kv in (spec ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = kv.Split('=');
            if (p.Length != 2 || !int.TryParse(p[0].Trim(), out int type)) continue;
            var idSub = p[1].Split('/');
            if (!uint.TryParse(idSub[0].Trim(), out uint id)) continue;
            int sub = idSub.Length > 1 && int.TryParse(idSub[1].Trim(), out var ss) ? ss : -1;
            yield return (kv.Trim(), PartModelName(type, id, sub));
        }
    }

    /// <summary>
    /// Our kept copy of a gear part model, made from the loaded game object of that name.
    /// One scene search finds all requested names at once (it's the expensive part — a hitch).
    /// </summary>
    private static void MakeTemplates(IEnumerable<string> names)
    {
        var want = new HashSet<string>(names.Where(n => n != null && !_templates.ContainsKey(n)));
        if (want.Count == 0) return;
        var found = new Dictionary<string, GameObject>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<GameObject>()))
        {
            var g = o?.TryCast<GameObject>();
            if (g == null || !want.Contains(g.name) || g.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
            // Prefer the prefab (not a copy placed in a scene).
            if (!found.ContainsKey(g.name) || !g.scene.IsValid()) found[g.name] = g;
        }
        foreach (var kv in found) _templates[kv.Key] = CopyModel(kv.Value, TemplateRoot, kv.Key, quiet: true);
        ModInfo.Log.LogInfo($"[TurretVariants] part models kept: {string.Join(", ", found.Keys)}" +
                            (found.Count < want.Count ? $"; not in memory now: {string.Join(", ", want.Where(n => !found.ContainsKey(n)))}" : ""));
    }

    /// <summary>A kept model for other modules (lobby icons): a part spec ("33=3") or model names ("Sight_19_t,Sight_10_t"); the first available.</summary>
    internal static GameObject KeptModel(string specOrNames)
    {
        var names = specOrNames.Contains('=')
            ? ParseParts(specOrNames).Select(p => p.Name).Where(n => n != null).ToList()
            : specOrNames.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
        MakeTemplates(names);
        foreach (var n in names) if (_templates.TryGetValue(n, out var t) && t != null) return t;
        return null;
    }

    /// <summary>Mounts the parts ("type=ID/sub, …") in the barrel cradle; placed by the kind's Model* settings.</summary>
    private static void AttachParts(Turret t, Transform head, string spec)
    {
        var parts = ParseParts(spec).ToList();
        if (parts.Count == 0) return;
        MakeTemplates(parts.Select(p => p.Name));
        var holder = new GameObject("GtfoPunk_Parts");
        holder.transform.SetParent(head, false);
        int n = 0;
        foreach (var (s, name) in parts)
        {
            if (name != null && _templates.TryGetValue(name, out var tpl) && tpl != null) { CopyModel(tpl, holder.transform, "GtfoPunk_Part", quiet: true); n++; }
            else ModInfo.Log.LogWarning($"[TurretVariants] {t.Kind.Label.Value}: model of part {s} ({name ?? "?"}) not available");
        }
        if (n == 0) { UnityEngine.Object.Destroy(holder); return; }
        t.PartsAttached = true;
        HideGun(t.Inst);
        void Place() => t.Kind.Place(holder);
        Place();
        t.Reapply.Add(Place);
        ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value}: parts in the cradle: {n}");
    }

    // ---------- prewarm: keep part models while they're in memory (lobby) ----------

    private static float _prewarmAt = -1f;
    private static eGameStateName _prewarmState;

    /// <summary>A few seconds into the lobby (gear models are loaded for the loadout) keep the part models we use.</summary>
    private static void PrewarmTick()
    {
        var state = GameStateManager.CurrentStateName;
        if (state != _prewarmState) { _prewarmState = state; _prewarmAt = state == eGameStateName.Lobby ? Time.time + 3f : -1f; }
        if (_prewarmAt < 0f || Time.time < _prewarmAt) return;
        _prewarmAt = -1f;
        try
        {
            var specs = new[] { _tracker.Enabled.Value ? _trackerParts.Value : "", _foam.Enabled.Value ? _foamParts.Value : "", _grenade.Enabled.Value ? _glParts.Value : "" };
            MakeTemplates(specs.SelectMany(ParseParts).Select(p => p.Name));
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] keeping part models: {e.Message}"); }
    }

    // ---------- model copies and placement ----------

    /// <summary>Model copy: meshes only — no scripts, physics or colliders. On the parent's layer.</summary>
    internal static GameObject CopyModel(GameObject prefab, Transform parent, string name, bool quiet = false)
    {
        var holder = new GameObject(name);
        holder.SetActive(false); // inactive while stripping — the copy's scripts won't start
        holder.transform.SetParent(parent, false);

        var copy = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        for (int pass = 0; pass < 3; pass++) // several passes: some components depend on others
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<MeshRenderer>() != null || c.TryCast<SkinnedMeshRenderer>() != null) continue;
                try { UnityEngine.Object.DestroyImmediate(c); } catch { }
            }
        int layer = parent != null ? parent.gameObject.layer : 0;
        foreach (var tr in copy.GetComponentsInChildren<Transform>(true)) { tr.gameObject.layer = layer; tr.gameObject.SetActive(true); }
        int n = 0;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) { r.enabled = true; r.forceRenderingOff = false; n++; }
        holder.SetActive(true);
        if (!quiet) ModInfo.Log.LogInfo($"[TurretVariants] model {name}: {n} meshes");
        return holder;
    }

    /// <summary>
    /// A node at the barrel root that turns with the sentry head and looks where the barrel looks.
    /// MuzzleAlign is at the barrel tip; the topmost ancestor named Front_* is the barrel part root (the "cradle").
    /// </summary>
    private static GameObject MakeHead(Turret t, out Transform cradle, out Transform muzzle)
    {
        cradle = null;
        muzzle = Firing(t)?.MuzzleAlign;
        if (muzzle == null) return null;
        cradle = muzzle.parent ?? muzzle;
        for (var tr = muzzle.parent; tr != null && tr != t.Inst.transform; tr = tr.parent)
            if (tr.name.StartsWith("Front_")) cradle = tr;
        var head = new GameObject("GtfoPunk_Head");
        head.transform.SetParent(cradle, false);
        head.transform.position = cradle.position;
        head.transform.rotation = muzzle.rotation;
        return head;
    }

    /// <summary>Moves the model so the bottom center of its bounds is at the seat point.</summary>
    private static void SeatModel(GameObject model, Vector3 seat)
    {
        Bounds b = default;
        bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        model.transform.position += seat - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    /// <summary>Hides the autocannon barrel and receiver (rendering only — attachment points stay).</summary>
    private static void HideGun(SentryGunInstance inst)
    {
        if (!_hideGun.Value) return;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled) continue;
            for (var tr = r.transform; tr != null && tr != inst.transform; tr = tr.parent)
            {
                string n = tr.name;
                if (n.StartsWith("GtfoPunk_")) break;
                if (n.StartsWith("Front_") || n.StartsWith("Receiver_") || (_hideSensor.Value && n.StartsWith("Targeting"))) { r.enabled = false; break; }
            }
        }
    }

    /// <summary>
    /// Searches loaded meshes for a model whose name (or an ancestor's) contains one of the words — for the turbine,
    /// whose item has no meshes of its own. Expensive; the result is kept as a template.
    /// </summary>
    private static GameObject FindLoadedModel(string keywords)
    {
        const string key = "search:turbine";
        if (_templates.TryGetValue(key, out var kept) && kept != null) return kept;
        var keys = (keywords ?? "").Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToArray();
        if (keys.Length == 0) return null;
        bool Match(string n) { n = (n ?? "").ToLowerInvariant(); return keys.Any(n.Contains); }
        Transform best = null;
        int bestCount = 0;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<MeshFilter>()))
        {
            var mf = o?.TryCast<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Transform root = null; // the topmost ancestor with a matching name is the model root
            for (var tr = mf.transform; tr != null; tr = tr.parent) if (Match(tr.name)) root = tr;
            if (root == null && Match(mf.sharedMesh.name)) root = mf.transform;
            if (root == null || root.name.StartsWith("GtfoPunk_")) continue;
            int n = root.GetComponentsInChildren<MeshFilter>(true).Length;
            if (n > bestCount) { best = root; bestCount = n; }
        }
        if (best == null) return null;
        ModInfo.Log.LogInfo($"[TurretVariants] turbine model found in memory: '{best.name}', meshes {bestCount}");
        return _templates[key] = CopyModel(best.gameObject, TemplateRoot, key, quiet: true);
    }
}
