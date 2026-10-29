using Gear;
using Il2CppInterop.Runtime.InteropTypes;
using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Quick melee (V by default): the character draws the melee weapon, performs the action and puts back what was in hand.
///  - Tap — shove/push (like RMB with the melee weapon).
///  - Hold while the melee weapon is being drawn — charged hit; release — strike.
/// Presses are injected both into the weapon's properties and into the game's key polling (InputMapper), so it works whichever way input is read.
/// </summary>
public class QuickMelee : ModuleBase
{
    public override string Id => "QuickMelee";
    public override string Description => "Quick melee on a key (V by default): tap — shove, hold — charged hit; then your gun comes back.";
    public override ModuleSide Side => ModuleSide.Client;

    private static ConfigEntry<KeyCode> _key;

    protected override void OnBindConfig()
    {
        _key = Bind("Key", KeyCode.V, "Quick melee key.");
    }

    private enum Phase { None, Equipping, Shove, Charge }

    private static Phase _phase;
    private static InventorySlot _prevSlot = InventorySlot.None;
    private static bool _keyHeld;          // key still held since it was pressed
    private static bool _fireFrame, _fireHeld, _aimFrame;
    private static bool _leftIdle, _forced;
    private static float _pressTime, _phaseStart, _deadline, _releasedAt;

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += Reset;
    }

    private static void Reset()
    {
        _phase = Phase.None; _prevSlot = InventorySlot.None;
        _keyHeld = _fireFrame = _fireHeld = _aimFrame = _leftIdle = _forced = false;
        _releasedAt = -1f;
    }

    private static MeleeWeaponFirstPerson CurrentMelee(PlayerAgent a)
    {
        var item = a?.Inventory?.WieldedItem;
        return item == null ? null : ((Il2CppObjectBase)item).TryCast<MeleeWeaponFirstPerson>();
    }

    private static void Tick()
    {
        var agent = GameUtil.LocalAgent;
        if (!GameUtil.InLevel || agent == null || !agent.Alive) { if (_phase != Phase.None) Reset(); return; }
        bool focus = FocusStateManager.CurrentState == eFocusState.FPS && !PlayerChatManager.InChatMode;
        _fireFrame = _aimFrame = false;
        if (_keyHeld && !Input.GetKey(_key.Value)) { _keyHeld = false; _releasedAt = Time.time; }

        switch (_phase)
        {
            case Phase.None:
                if (!focus || !Input.GetKeyDown(_key.Value)) return;
                _keyHeld = true;
                _releasedAt = -1f;
                _pressTime = Time.time;
                var slot = agent.Inventory.WieldedSlot;
                _prevSlot = slot == InventorySlot.GearMelee ? InventorySlot.None : slot;
                if (slot != InventorySlot.GearMelee) agent.Sync.WantsToWieldSlot(InventorySlot.GearMelee);
                _phase = Phase.Equipping;
                _deadline = Time.time + 2f;
                break;

            case Phase.Equipping:
            {
                var melee = CurrentMelee(agent);
                bool ready = melee != null && agent.FPItemHolder != null && !agent.FPItemHolder.ItemIsBusy
                             && melee.CurrentStateName == eMeleeWeaponState.Idle
                             && (!_keyHeld || Time.time - _pressTime > HoldThreshold); // short press = shove
                if (ready)
                {
                    _leftIdle = _forced = false;
                    _phaseStart = Time.time;
                    _deadline = Time.time + 5f;
                    if (_keyHeld) { _phase = Phase.Charge; _fireFrame = _fireHeld = true; }
                    else { _phase = Phase.Shove; _aimFrame = true; }
                }
                else if (Time.time > _deadline) Back(agent);
                break;
            }

            case Phase.Shove:
            {
                var m = CurrentMelee(agent);
                if (m == null) { Reset(); return; }
                var st = m.CurrentStateName;
                if (st != eMeleeWeaponState.Idle) _leftIdle = true;
                else if (_leftIdle) { Back(agent); return; }
                // If the game didn't read the injected RMB — push directly.
                if (!_leftIdle && !_forced && Time.time - _phaseStart > 0.15f) { _forced = true; m.ChangeState(eMeleeWeaponState.Push); }
                if (Time.time > _deadline) Back(agent);
                break;
            }

            case Phase.Charge:
            {
                var m = CurrentMelee(agent);
                if (m == null) { Reset(); return; }
                _fireHeld = _keyHeld;
                var st = m.CurrentStateName;
                if (st != eMeleeWeaponState.Idle) _leftIdle = true;
                else if (_leftIdle) { Back(agent); return; }
                // Released, but the wind-up is still hanging — release the strike ourselves.
                if (!_keyHeld && _releasedAt > 0f && Time.time - _releasedAt > 0.1f)
                {
                    if (st == eMeleeWeaponState.AttackChargeUpLeft) m.ChangeState(eMeleeWeaponState.AttackChargeReleaseLeft);
                    else if (st == eMeleeWeaponState.AttackChargeUpRight) m.ChangeState(eMeleeWeaponState.AttackChargeReleaseRight);
                }
                if (Time.time > _deadline) Back(agent);
                break;
            }
        }
    }

    private static void Back(PlayerAgent agent)
    {
        if (_prevSlot != InventorySlot.None && _prevSlot != InventorySlot.GearMelee)
            agent.Sync.WantsToWieldSlot(_prevSlot);
        Reset();
    }

    private const float HoldThreshold = 0.25f;

    private static bool Active => _phase == Phase.Shove || _phase == Phase.Charge;

    private static bool Ours(MeleeWeaponFirstPerson m) =>
        Active && m != null && GameUtil.IsLocalHuman(m.Owner);

    // ---- injection into melee weapon properties ----
    [HarmonyPatch(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.FireButtonPressed), MethodType.Getter)]
    private static class Patch_FirePressed
    {
        private static void Postfix(MeleeWeaponFirstPerson __instance, ref bool __result) { if (_fireFrame && Ours(__instance)) __result = true; }
    }

    [HarmonyPatch(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.FireButton), MethodType.Getter)]
    private static class Patch_FireHeld
    {
        private static void Postfix(MeleeWeaponFirstPerson __instance, ref bool __result) { if ((_fireHeld || _fireFrame) && Ours(__instance)) __result = true; }
    }

    [HarmonyPatch(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.AimButtonPressed), MethodType.Getter)]
    private static class Patch_AimPressed
    {
        private static void Postfix(MeleeWeaponFirstPerson __instance, ref bool __result) { if (_aimFrame && Ours(__instance)) __result = true; }
    }

    // ---- injection into the game's key polling ----
    private static void Inject(InputAction action, bool down, ref bool result)
    {
        if (!Active || result) return;
        if (action == InputAction.Fire) result = down ? _fireFrame : (_fireHeld || _fireFrame);
        else if (action == InputAction.Aim) result = _aimFrame;
    }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonKeyMouse))]
    private static class Patch_GB1 { private static void Postfix(InputAction action, ref bool __result) => Inject(action, false, ref __result); }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonKeyMouseGamepad))]
    private static class Patch_GB2 { private static void Postfix(InputAction action, ref bool __result) => Inject(action, false, ref __result); }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonDownKeyMouse))]
    private static class Patch_GBD1 { private static void Postfix(InputAction action, ref bool __result) => Inject(action, true, ref __result); }

    [HarmonyPatch(typeof(InputMapper), nameof(InputMapper.GetButtonDownKeyMouseGamepad))]
    private static class Patch_GBD2 { private static void Postfix(InputAction action, ref bool __result) => Inject(action, true, ref __result); }
}
