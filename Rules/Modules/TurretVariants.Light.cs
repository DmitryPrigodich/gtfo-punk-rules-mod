using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>TL1 searchlight: a spotlight that turns after enemies in its sector; the beam disturbs and wakes sleepers.</summary>
public partial class TurretVariants
{
    private static uint _lampId = uint.MaxValue;

    private static bool SetupLight(Turret t)
    {
        var head = MakeHead(t, out var cradle, out var muzzle);
        if (head == null) return false;

        if (_lampId == uint.MaxValue) _lampId = FindItemId(_lampItem.Value);
        GameObject lamp = null;
        try { lamp = AttachItemModel(head.transform, _lampId, "GtfoPunk_Lamp"); }
        catch (Exception e) { ModInfo.Log.LogWarning($"[TurretVariants] lamp model: {e.Message}"); }
        HideGun(t.Inst);
        ModInfo.Log.LogInfo($"[TurretVariants] lamp on '{cradle.name}', {Vector3.Distance(cradle.position, muzzle.position):0.00} m from the muzzle point");

        var beam = new GameObject("GtfoPunk_Searchlight");
        beam.transform.SetParent(head.transform, false);
        var light = MakeLight(beam, LightType.Spot, _lightRange.Value, _lightIntensity.Value, new Color(1f, 0.95f, 0.85f));
        light.spotAngle = _lightAngle.Value;

        GameObject glowGo = null;
        if (_lensGlow.Value > 0f)
        {
            glowGo = new GameObject("GtfoPunk_LampGlow");
            glowGo.transform.SetParent(head.transform, false);
            t.Glow = MakeLight(glowGo, LightType.Point, 0.5f, _lensGlow.Value, new Color(1f, 0.95f, 0.85f));
        }

        // Positions come from the config and are re-applied when it's edited on the fly.
        void Place()
        {
            t.Kind.Place(lamp);
            var lens = t.Kind.Offset;
            beam.transform.localPosition = lens + Vector3.forward * _lightForward.Value; // in front of the glass, not inside the model
            light.range = _lightRange.Value;
            light.spotAngle = _lightAngle.Value;
            if (glowGo != null)
            {
                glowGo.transform.localPosition = lens + Vector3.forward * _lensGlowForward.Value;
                t.Glow.intensity = _lensGlow.Value;
            }
        }
        Place();
        t.Reapply.Add(Place);

        t.Extra = beam;
        t.Light = light;
        return true;
    }

    /// <summary>A light that GTFO actually renders (it draws lights through its own clustered system).</summary>
    private static Light MakeLight(GameObject go, LightType type, float range, float intensity, Color color)
    {
        var l = go.AddComponent<Light>();
        l.type = type;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.None;
        CL_Light.Create(type == LightType.Spot ? eClusteredLightType.Spot : eClusteredLightType.Point, go, l);
        return l;
    }

    private static void LightTick(Turret t)
    {
        if (t.Light == null) return;
        bool target = t.Inst.m_detection != null && t.Inst.m_detection.HasTarget;
        t.Light.intensity = _lightIntensity.Value * (target ? 1.6f : 1f);
        if (!SNetwork.SNet.IsMaster || Time.time < t.NextDisturb) return;
        t.NextDisturb = Time.time + Math.Max(0.3f, _lightInterval.Value);
        if (!Enum.TryParse<NM_NoiseType>(_lightNoise.Value, true, out var type)) return; // Off
        Expose(t, InBeam(t), type, _lightWakeAfter.Value);
    }

    /// <summary>Enemies inside the beam cone with a clear line from the lamp.</summary>
    private static IEnumerable<Enemies.EnemyAgent> InBeam(Turret t)
    {
        var tr = t.Light.transform;
        float half = _lightAngle.Value * 0.5f;
        foreach (var e in EnemiesNear(tr.position, _lightRange.Value))
        {
            if (Vector3.Angle(tr.forward, e.EyePosition - tr.position) > half) continue;
            if (Physics.Linecast(tr.position, e.EyePosition, LayerManager.MASK_WORLD)) continue;
            yield return e;
        }
    }
}
