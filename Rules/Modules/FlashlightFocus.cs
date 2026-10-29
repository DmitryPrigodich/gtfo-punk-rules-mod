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
/// The focus is a copy of the flashlight part's own setting with the angle / range / brightness changed, put on the
/// gun's flashlight part; the game then applies it the usual way. Local only: others see your light as usual.
/// </summary>
public class FlashlightFocus : ModuleBase
{
    public override string Id => "FlashlightFocus";
    public override string Description => "Gun flashlight focus: hold the flashlight key and turn the mouse wheel — wide and near or narrow and far.";
    public override ModuleSide Side => ModuleSide.Client;

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
        FrameTicker.OnLevelExit += () => { _step = 0; _holding = false; _releasePending = false; _base.Clear(); };
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

    /// <summary>The game applies a gun's flashlight when it's wielded: give it the focused copy of its own setting.</summary>
    [HarmonyPatch(typeof(PlayerInventoryBase), nameof(PlayerInventoryBase.OnItemEquippableFlashlightWielded))]
    private static class Patch_Wield
    {
        private static void Prefix(PlayerInventoryBase __instance, GearPartFlashlight flashlight)
        {
            try
            {
                if (flashlight == null || __instance == null || !__instance.Owner.IsLocallyOwned) return;
                if (!_base.TryGetValue(flashlight.Pointer, out var own)) _base[flashlight.Pointer] = own = flashlight.m_settingsID;
                flashlight.m_settingsID = _step == 0 ? own : Focused(own, _step);
            }
            catch (Exception e) { ModInfo.Log.LogWarning($"[FlashlightFocus] {e.Message}"); }
        }
    }

    private static void Refocus()
    {
        var agent = GameUtil.LocalAgent;
        var inv = agent?.Inventory;
        var part = inv?.m_currentGearPartFlashlight;
        var item = inv?.WieldedItem;
        if (part == null || item == null) return;
        inv.OnItemEquippableFlashlightWielded(item, part); // our prefix puts the focused setting on
        var s = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(part.m_settingsID);
        int max = Math.Max(1, _steps.Value);
        string pos = _step == 0 ? Lang.T("as is", "как есть") : _step > 0 ? Lang.T($"narrow {_step}/{max}", $"узко {_step}/{max}") : Lang.T($"wide {-_step}/{max}", $"широко {-_step}/{max}");
        try
        {
            var gui = GuiManager.InteractionLayer;
            gui.SetMessage(Lang.T("FLASHLIGHT: ", "ФОНАРЬ: ") + pos + (s != null ? $"  ({s.angle:0}°, {s.range:0} {Lang.T("m", "м")})" : ""), ePUIMessageStyle.Default, 10);
            gui.MessageVisible = true;
            _hideMsgAt = Time.time + 1.2f;
        }
        catch { }
    }

    /// <summary>A copy of the setting with the focus applied (made once per setting and position).</summary>
    private static uint Focused(uint src, int step)
    {
        if (_focused.TryGetValue((src, step), out var id)) return id;
        var s = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlock(src);
        if (s == null) return src;
        int max = Math.Max(1, _steps.Value);
        float f = Mathf.Pow(Math.Max(1.05f, _max.Value), (float)step / max); // >1 — narrower
        float k = Mathf.Pow(f, Mathf.Clamp(_reach.Value, 0f, 2f));
        string key = $"GtfoPunk_Focus_{src}_{step}";
        if (GameDataBlockBase<FlashlightSettingsDataBlock>.HasBlock(key)) id = GameDataBlockBase<FlashlightSettingsDataBlock>.GetBlockID(key);
        else
        {
            var w = GearFactory.CloneBlock(s);
            w.persistentID = GearFactory.StableId<FlashlightSettingsDataBlock>(key);
            w.name = key;
            w.angle = Mathf.Clamp(s.angle / f, 5f, 100f);
            w.range = s.range * k;
            w.intensity = s.intensity * k;
            GameDataBlockBase<FlashlightSettingsDataBlock>.AddBlock(w);
            id = w.persistentID;
            ModInfo.Log.LogInfo($"[FlashlightFocus] {s.name} at {step:+0;-0}: angle {s.angle:0} → {w.angle:0}°, range {s.range:0.#} → {w.range:0.#} m, brightness {s.intensity:0.##} → {w.intensity:0.##}");
        }
        _focused[(src, step)] = id;
        return id;
    }
}
