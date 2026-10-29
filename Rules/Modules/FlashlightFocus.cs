using GameData;
using Gear;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Gun flashlight focus, like turning the lens of a real one: hold the flashlight key and turn the mouse wheel —
/// wide and short or narrow and far. A narrower beam reaches further and is brighter (the "amount of light" stays
/// about the same), a wider one lights more around you but less far.
///
/// How: the flashlight key's press is held back while it's down (the wheel doesn't switch weapons meanwhile); released
/// without turning the wheel — the game gets the press and toggles the light as usual (on release instead of press).
/// Gun light: a copy of the flashlight part's own setting with the angle / range / brightness changed, put on the
/// gun's flashlight part; the game then applies it the usual way. Helmet light (melee, tools): the light itself is
/// changed after the game sets it up, from the HelmetLight setting. Each player's focus is sent to the others, so
/// everyone sees everyone's beam as it is (Net type 5).
/// </summary>
public class FlashlightFocus : ModuleBase
{
    public override string Id => "FlashlightFocus";
    public override string Description => "Flashlight focus: hold the flashlight key and turn the mouse wheel — wide and near or narrow and far. Others see your beam too.";
    public override ModuleSide Side => ModuleSide.Everyone;

    private static ConfigEntry<int> _steps;
    private static ConfigEntry<float> _max, _reach;

    protected override void OnBindConfig()
    {
        _steps = Bind("Steps", 2, "Focus positions on each side of the flashlight's own (2 — five positions in all).");
        _max = Bind("MaxFocus", 2f, "The narrowest position: the beam angle divided by this (the widest — multiplied by it; at most 100°).");
        _reach = Bind("ReachExponent", 0.8f, "How range and brightness follow the focus: × focus^this (0.8 — half the angle gives ×1.74 range and brightness).");
    }

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => { _step = 0; _holding = false; _releasePending = false; _base.Clear(); _remote.Clear(); _helmet.Clear(); };
        Net.On(Net.FlashlightFocus, OnReceived);
    }

    private static int _step;                 // 0 — the flashlight's own; + narrower, − wider
    private static bool _holding, _scrolled, _releasePending;
    private static float _hideMsgAt = -1f;
    private static int _passedFrame = -1;

    // ---------- input: the flashlight press goes through on release; the wheel focuses while the key is held ----------

    private static void Down(InputAction action, ref bool result)
    {
        if (action != InputAction.Flashlight) return;
        if (Time.frameCount == _passedFrame) { result = true; return; } // our delayed press (the game may ask more than once a frame)
        if (result && GameUtil.InLevel) { result = false; _holding = true; _scrolled = false; return; }
        if (_releasePending) { _releasePending = false; _passedFrame = Time.frameCount; result = true; }
    }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonDownKeyMouse))]
    private static class Patch_DownKM { private static void Postfix(InputAction action, ref bool __result) => Down(action, ref __result); }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonDownKeyMouseGamepad))]
    private static class Patch_DownKMG { private static void Postfix(InputAction action, ref bool __result) => Down(action, ref __result); }

    /// <summary>While the flashlight key is held the wheel focuses — it doesn't switch weapons.</summary>
    private static void Axis(InputAction action, ref float result)
    {
        if (_holding && action == InputAction.ScrollItems) result = 0f;
    }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetAxisKeyMouse))]
    private static class Patch_AxisKM { private static void Postfix(InputAction action, ref float __result) => Axis(action, ref __result); }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetAxisKeyMouseGamepad))]
    private static class Patch_AxisKMG { private static void Postfix(InputAction action, ref float __result) => Axis(action, ref __result); }

    private static void Tick()
    {
        if (_hideMsgAt > 0f && Time.time > _hideMsgAt) { _hideMsgAt = -1f; try { GuiManager.InteractionLayer.MessageVisible = false; } catch { } }
        if (!_holding) return;
        bool held;
        try { held = InputMapper.GetButtonKeyMouseGamepad(InputAction.Flashlight); } catch { held = false; }
        if (!held)
        {
            _holding = false;
            if (!_scrolled) _releasePending = true; // a plain press: the game toggles the light on its next check
            return;
        }
        float wheel = Input.mouseScrollDelta.y;
        if (Math.Abs(wheel) < 0.01f) return;
        _scrolled = true;
        int max = Math.Max(1, _steps.Value);
        int step = Math.Clamp(_step + (wheel > 0f ? 1 : -1), -max, max);
        if (step == _step) return;
        _step = step;
        try { Refocus(); } catch (Exception e) { ModInfo.Log.LogWarning($"[FlashlightFocus] {e.Message}"); }
    }

    // ---------- applying the focus ----------

    /// <summary>Each flashlight part's own setting (the part keeps our copy once focused).</summary>
    private static readonly Dictionary<IntPtr, uint> _base = new();
    private static readonly Dictionary<(uint, int), uint> _focused = new();
    /// <summary>Other players' focus, by player lookup.</summary>
    private static readonly Dictionary<ulong, int> _remote = new();
    /// <summary>Inventories whose light is the helmet light right now.</summary>
    private static readonly HashSet<IntPtr> _helmet = new();

    private static int StepOf(PlayerInventoryBase inv)
    {
        var owner = inv?.Owner;
        if (owner == null) return 0;
        if (owner.IsLocallyOwned) return _step;
        try { return owner.Owner != null && _remote.TryGetValue(owner.Owner.Lookup, out var r) ? r : 0; } catch { return 0; }
    }

    /// <summary>The game applies a gun's flashlight when it's wielded: give it the focused copy of its own setting.</summary>
    [HarmonyPatch(typeof(PlayerInventoryBase), nameof(PlayerInventoryBase.OnItemEquippableFlashlightWielded))]
    private static class Patch_Wield
    {
        private static void Prefix(PlayerInventoryBase __instance, GearPartFlashlight flashlight)
        {
            try
            {
                if (flashlight == null || __instance == null) return;
                _helmet.Remove(__instance.Pointer);
                if (!_base.TryGetValue(flashlight.Pointer, out var own)) _base[flashlight.Pointer] = own = flashlight.m_settingsID;
                int step = StepOf(__instance);
                flashlight.m_settingsID = step == 0 ? own : Focused(own, step);
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[FlashlightFocus] {e.Message}"); }
        }
    }

    /// <summary>The helmet light (no gun light in hand): focus the light the game has just set up.</summary>
    [HarmonyPatch(typeof(PlayerInventoryBase), nameof(PlayerInventoryBase.PrepareHelmetFlashlight))]
    private static class Patch_Helmet
    {
        private static void Postfix(PlayerInventoryBase __instance)
        {
            if (__instance == null) return;
            _helmet.Add(__instance.Pointer);
            try { FocusHelmet(__instance, StepOf(__instance)); }
            catch (Exception e) { ModInfo.Log.LogWarning($"[FlashlightFocus] helmet: {e.Message}"); }
        }
    }

    private static FlashlightSettingsDataBlock _helmetData;
    private static bool _helmetLogged;

    private static FlashlightSettingsDataBlock FocusHelmet(PlayerInventoryBase inv, int step)
    {
        _helmetData ??= GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock("HelmetLight") ?? GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(1u);
        var l = inv.m_flashlight;
        if (_helmetData == null || l == null) return null;
        var (angle, range, intensity) = Focus(_helmetData, step);
        l.spotAngle = angle;
        l.range = range;
        l.intensity = intensity;
        try { inv.m_flashlightCLight?.m_clusterLight?.UpdateData(); } catch { }
        if (!_helmetLogged && step != 0) { _helmetLogged = true; ModInfo.Log.LogInfo($"[FlashlightFocus] helmet light at {step:+0;-0}: {angle:0}°, {range:0.#} m, brightness {intensity:0.##}"); }
        return _helmetData;
    }

    /// <summary>Re-applies a player's focus to the light in their hands (gun or helmet).</summary>
    private static (float Angle, float Range)? Apply(PlayerInventoryBase inv, int step)
    {
        if (inv == null) return null;
        if (_helmet.Contains(inv.Pointer))
        {
            var h = FocusHelmet(inv, step);
            if (h == null) return null;
            var (a, r, _) = Focus(h, step);
            return (a, r);
        }
        var part = inv.m_currentGearPartFlashlight;
        var item = inv.WieldedItem;
        if (part == null || item == null) return null;
        inv.OnItemEquippableFlashlightWielded(item, part); // our prefix puts the focused setting on
        var s = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(part.m_settingsID);
        return s == null ? null : (s.angle, s.range);
    }

    private static void Refocus()
    {
        var res = Apply(GameUtil.LocalAgent?.Inventory, _step);
        Net.SendToAll(Net.FlashlightFocus, new[] { unchecked((byte)(sbyte)_step) });
        int max = Math.Max(1, _steps.Value);
        string pos = _step == 0 ? Lang.T("as is", "как есть") : _step > 0 ? Lang.T($"narrow {_step}/{max}", $"узко {_step}/{max}") : Lang.T($"wide {-_step}/{max}", $"широко {-_step}/{max}");
        try
        {
            var gui = GuiManager.InteractionLayer;
            gui.SetMessage(Lang.T("FLASHLIGHT: ", "ФОНАРЬ: ") + pos + (res is { } v ? $"  ({v.Angle:0}°, {v.Range:0} {Lang.T("m", "м")})" : ""), ePUIMessageStyle.Default, 10);
            gui.MessageVisible = true;
            _hideMsgAt = Time.time + 1.2f;
        }
        catch { }
    }

    /// <summary>Another player turned their lens.</summary>
    private static void OnReceived(ulong from, byte[] payload)
    {
        if (payload.Length < 1) return;
        int step = (sbyte)payload[0];
        _remote[from] = step;
        try
        {
            foreach (var p in SNetwork.SNet.Slots.SlottedPlayers)
                if (p != null && p.Lookup == from && GameUtil.AgentOf(p) is { } a) { Apply(a.Inventory, step); break; }
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[FlashlightFocus] remote: {e.Message}"); }
    }

    /// <summary>Angle, range and brightness of a setting at a focus position.</summary>
    private static (float Angle, float Range, float Intensity) Focus(FlashlightSettingsDataBlock s, int step)
    {
        if (step == 0) return (s.angle, s.range, s.intensity);
        int max = Math.Max(1, _steps.Value);
        float f = Mathf.Pow(Math.Max(1.05f, _max.Value), (float)step / max); // >1 — narrower
        float k = Mathf.Pow(f, Mathf.Clamp(_reach.Value, 0f, 2f));
        return (Mathf.Clamp(s.angle / f, 5f, 100f), s.range * k, s.intensity * k);
    }

    /// <summary>A copy of the setting with the focus applied (made once per setting and position).</summary>
    private static uint Focused(uint src, int step)
    {
        if (_focused.TryGetValue((src, step), out var id)) return id;
        var s = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(src);
        if (s == null) return src;
        var (angle, range, intensity) = Focus(s, step);
        string key = $"GtfoPunk_Focus_{src}_{step}";
        if (GameDataBlockBase<FlashlightSettingsDataBlock>.HasBlock(key)) id = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlockID(key);
        else
        {
            var w = GearFactory.CloneBlock(s);
            w.persistentID = GearFactory.StableId<FlashlightSettingsDataBlock>(key);
            w.name = key;
            w.angle = angle;
            w.range = range;
            w.intensity = intensity;
            GameDataBlockBase<FlashlightSettingsDataBlock>.AddBlock(w);
            id = w.persistentID;
            ModInfo.Log.LogInfo($"[FlashlightFocus] {s.name} at {step:+0;-0}: angle {s.angle:0} → {w.angle:0}°, range {s.range:0.#} → {w.range:0.#} m, brightness {s.intensity:0.##} → {w.intensity:0.##}");
        }
        _focused[(src, step)] = id;
        return id;
    }
}
