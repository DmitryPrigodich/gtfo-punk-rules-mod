using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// TF I / TF II fog repellers: the fog-free sphere of a thrown fog repeller, endless, on the sentry; a repeller model
/// (TF I) or a scaled-down turbine (TF II) in the cradle, a dim warm bulb, and a hum that disturbs and wakes sleepers.
/// </summary>
public partial class TurretVariants
{
    private const uint FogRepellerItemId = 117; // CONSUMABLE_FogRepeller
    private const uint FogTurbineItemId = 133;  // Carry_HeavyFogRepeller — the big mission turbine

    private static bool SetupFog(Turret t)
    {
        var sphereSource = FogSpherePrefab();
        if (sphereSource == null) return false;
        var muzzle = Firing(t)?.MuzzleAlign;
        // Wait until the sentry assembles its parts (otherwise nothing to hide and nowhere to attach).
        if (muzzle == null && Time.time - t.SpawnedAt < 4f) return false;
        var top = muzzle != null ? muzzle.position : t.Inst.transform.position + Vector3.up * 0.6f;

        var go = UnityEngine.Object.Instantiate(sphereSource, top, Quaternion.identity);
        var sphere = go.GetComponentInChildren<FogRepeller_Sphere>(true);
        if (sphere == null) { UnityEngine.Object.Destroy(go); return false; }
        go.transform.SetParent(t.Inst.transform, true);
        go.name = "GtfoPunk_FogSphere";

        // Of the thrown repeller keep only the sphere: no physics, colliders or model.
        foreach (var c in go.GetComponentsInChildren<FogRepellerInstance>(true)) UnityEngine.Object.Destroy(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
        foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        sphere.InfiniteDuration = true;
        sphere.Range = t.Mode == Mode.FogTurbine ? _turbineRadius.Value : _fogRadius.Value;
        sphere.gameObject.SetActive(true);
        sphere.StartRepelling();
        t.Extra = go;
        t.Sphere = sphere;

        // Models only once the sphere is in place (set-up is retried every frame until it succeeds).
        HideGun(t.Inst);
        try { AttachFogModel(t, top); }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] {t.Kind.Label.Value} model: {e.Message}"); }
        return true;
    }

    /// <summary>The thrown fog repeller variant that carries the fog sphere (not just any variant with meshes).</summary>
    private static GameObject FogSpherePrefab()
    {
        var perMode = ItemSpawnManager.m_loadedPrefabsPerItemMode;
        if (perMode == null || perMode.Length <= (int)ItemMode.Instance) return null;
        var dict = perMode[(int)ItemMode.Instance];
        if (dict == null || !dict.ContainsKey(FogRepellerItemId)) return null;
        var list = dict[FogRepellerItemId];
        for (int k = 0; list != null && k < list.Count; k++)
            if (list[k] != null && list[k].GetComponentInChildren<FogRepeller_Sphere>(true) != null) return list[k];
        return null;
    }

    /// <summary>The repeller / turbine model on top and the bulb.</summary>
    private static void AttachFogModel(Turret t, Vector3 top)
    {
        var anchor = new GameObject("GtfoPunk_FogTop");
        anchor.transform.SetParent(t.Inst.transform, false);
        anchor.transform.position = top;
        // Exactly vertical even if the sentry stands on a slope; turned only by the sentry's heading.
        anchor.transform.rotation = Quaternion.Euler(0f, t.Inst.transform.eulerAngles.y, 0f);

        GameObject model = null;
        if (t.Mode == Mode.FogTurbine)
        {
            var prefab = ItemModel(FogTurbineItemId) ?? FindLoadedModel(TurbineModelSearch);
            if (prefab != null)
            {
                model = CopyModel(prefab, anchor.transform, "GtfoPunk_FogModel");
                // The turbine's bottom sits in the barrel cradle (barrel hidden), plus the configured offset.
                var head = MakeHead(t, out var cradle, out _);
                if (head != null) UnityEngine.Object.Destroy(head);
                var cradlePos = cradle != null ? cradle.position : top;
                var tb = model;
                void Seat()
                {
                    tb.transform.localPosition = Vector3.zero;
                    tb.transform.localRotation = t.Kind.Rotation;
                    tb.transform.localScale = Vector3.one * t.Kind.Scale;
                    SeatModel(tb, cradlePos + anchor.transform.rotation * t.Kind.Offset);
                }
                Seat();
                t.Reapply.Add(Seat);
            }
            else ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value}: no turbine model in memory — using the repeller model");
        }
        if (model == null)
        {
            // The thrown (Instance) repeller stands upright; the pickup model lies tilted.
            model = AttachItemModel(anchor.transform, FogRepellerItemId, "GtfoPunk_FogModel", InstanceFirst);
            var kind = t.Mode == Mode.FogTurbine ? _fog : t.Kind; // the repeller model is placed like on TF I
            void Place() => kind.Place(model);
            Place();
            t.Reapply.Add(Place);
        }
        if (_fogBulb.Value > 0f)
        {
            var bulb = new GameObject("GtfoPunk_FogLamp");
            bulb.transform.SetParent(anchor.transform, false);
            bulb.transform.localPosition = _fog.Offset + Vector3.up * 0.15f;
            t.FogBulb = MakeLight(bulb, LightType.Point, 5f, _fogBulb.Value, new Color(1f, 0.75f, 0.45f)); // warm incandescent
        }
    }

    private static int _humLogs;

    /// <summary>The hum: sleepers within HumRadius (through walls) are disturbed every HumInterval; HumWakeAfter seconds in a row wakes them.</summary>
    private static void FogTick(Turret t)
    {
        if (!SNetwork.SNet.IsMaster || Time.time < t.NextDisturb) return;
        bool turbine = t.Mode == Mode.FogTurbine;
        var (noise, radius, interval, wake) = turbine
            ? (_turbineHum.Value, _turbineHumRadius.Value, _turbineHumInterval.Value, _turbineHumWake.Value)
            : (_fogHum.Value, _fogHumRadius.Value, _fogHumInterval.Value, _fogHumWake.Value);
        t.NextDisturb = Time.time + Math.Max(0.3f, interval);
        if (radius <= 0f || !Enum.TryParse<NM_NoiseType>(noise, true, out var type)) return; // Off
        var near = EnemiesNear(t.Inst.transform.position + Vector3.up * 0.5f, radius).ToList();
        int woke = Expose(t, near, type, wake);
        if (near.Count > 0 && _humLogs++ < 5) ModInfo.Log.LogInfo($"[TurretVariants] {t.Kind.Label.Value} hum: {near.Count} enemies within {radius:0} m, woken {woke}");
    }
}
