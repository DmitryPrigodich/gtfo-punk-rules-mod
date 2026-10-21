using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameData;
using Gear;
using GtfoPunk.Core;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Switch fire mode with a key (B by default): semi → burst → auto → … in a loop.
/// Works for any player firearm (not sentries). Data comes from the weapon itself,
/// the rate of fire is always the native one. Burst — only for weapons with a native burst (option BurstForAll).
/// </summary>
public class FireModes : ModuleBase
{
    public override string Id => "FireModes";
    public override string Description => "Switch fire mode (semi / burst / auto) with a key, B by default.";
    public override ModuleSide Side => ModuleSide.Client;
    public override IReadOnlyList<string> ReplacedBy => new[] { "Dinorush.ExtraWeaponCustomization", "ExtraWeaponCustomization" };

    private static BepInEx.Configuration.ConfigEntry<KeyCode> _key;
    private static BepInEx.Configuration.ConfigEntry<string> _modes;
    private static BepInEx.Configuration.ConfigEntry<int> _burstCount;
    private static BepInEx.Configuration.ConfigEntry<float> _burstDelay;
    private static BepInEx.Configuration.ConfigEntry<bool> _burstForAll;

    protected override void OnBindConfig()
    {
        _key = Bind("Key", KeyCode.B, "Fire mode switch key.");
        _modes = Bind("Modes", "Semi,Burst,Auto", "Available modes in order (Semi, Burst, Auto). The weapon's native mode is always included.");
        _burstCount = Bind("BurstShots", 3, "Shots per burst (for weapons without their own burst).");
        _burstDelay = Bind("BurstDelay", 0.3f, "Pause between bursts, s (for weapons without their own burst).");
        _burstForAll = Bind("BurstForAll", false, "Give burst to weapons that don't have it natively. Off: burst only for native-burst weapons, others get semi/auto.");
    }

    private class WeaponModes
    {
        public BulletWeaponArchetype Original;
        public readonly List<eWeaponFireMode> Order = new();
        public readonly Dictionary<eWeaponFireMode, BulletWeaponArchetype> Cache = new();
        public int Index;
    }

    private static readonly Dictionary<IntPtr, WeaponModes> _weapons = new();
    private static float _hideMsgAt = -1f;

    protected override void OnEnable()
    {
        FrameTicker.OnUpdate += Tick;
        FrameTicker.OnLevelExit += () => _weapons.Clear();
    }

    private static void Tick()
    {
        if (_hideMsgAt > 0f && Time.time > _hideMsgAt)
        {
            _hideMsgAt = -1f;
            GuiManager.InteractionLayer.MessageVisible = false;
        }
        if (!GameUtil.InLevel || PlayerChatManager.InChatMode || FocusStateManager.CurrentState != eFocusState.FPS) return;
        if (!Input.GetKeyDown(_key.Value)) return;

        var item = GameUtil.LocalAgent?.Inventory?.WieldedItem;
        var gun = item == null ? null : ((Il2CppObjectBase)item).TryCast<BulletWeapon>();
        if (gun == null || gun.m_archeType == null) return;

        try { Cycle(gun); }
        catch (Exception e) { ModInfo.Log.LogError($"[FireModes] {e}"); }
    }

    private static WeaponModes ModesOf(BulletWeapon gun)
    {
        if (_weapons.TryGetValue(gun.Pointer, out var wm)) return wm;
        wm = new WeaponModes { Original = gun.m_archeType };
        var native = gun.m_archeType.m_archetypeData.FireMode;
        foreach (var s in _modes.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<eWeaponFireMode>(s, true, out var m) && m is eWeaponFireMode.Semi or eWeaponFireMode.Burst or eWeaponFireMode.Auto && !wm.Order.Contains(m)
                && (m != eWeaponFireMode.Burst || native == eWeaponFireMode.Burst || _burstForAll.Value))
                wm.Order.Add(m);
        if (!wm.Order.Contains(native)) wm.Order.Insert(0, native);
        wm.Index = wm.Order.IndexOf(native);
        wm.Cache[native] = wm.Original;
        _weapons[gun.Pointer] = wm;
        return wm;
    }

    private static void Cycle(BulletWeapon gun)
    {
        var wm = ModesOf(gun);
        if (wm.Order.Count < 2) return;
        var old = gun.m_archeType;
        if (old.m_firing) return; // don't switch in the middle of a burst/firing

        wm.Index = (wm.Index + 1) % wm.Order.Count;
        var mode = wm.Order[wm.Index];
        if (!wm.Cache.TryGetValue(mode, out var arch))
        {
            arch = Create(gun, wm.Original.m_archetypeData, mode);
            wm.Cache[mode] = arch;
        }

        arch.m_nextShotTimer = old.m_nextShotTimer;
        arch.m_nextBurstTimer = old.m_nextBurstTimer;
        arch.m_fireHeld = false;
        arch.m_firePressed = false;
        arch.m_firing = false;
        arch.m_readyToFire = old.m_readyToFire;
        gun.m_archeType = arch;

        ShowMode(mode);
    }

    private static BulletWeaponArchetype Create(BulletWeapon gun, ArchetypeDataBlock src, eWeaponFireMode mode)
    {
        var data = Clone(src);
        data.FireMode = mode;
        if (mode == eWeaponFireMode.Burst && src.FireMode != eWeaponFireMode.Burst)
        {
            data.BurstShotCount = Math.Max(2, _burstCount.Value);
            data.BurstDelay = Math.Max(0.05f, _burstDelay.Value);
        }
        BulletWeaponArchetype arch = mode switch
        {
            eWeaponFireMode.Semi => new BWA_Semi(data),
            eWeaponFireMode.Burst => new BWA_Burst(data),
            _ => new BWA_Auto(data),
        };
        arch.Setup(gun);
        if (mode == eWeaponFireMode.Burst) ((Il2CppObjectBase)arch).Cast<BWA_Burst>().m_burstMax = data.BurstShotCount;
        arch.SetOwner(gun.Owner);
        return arch;
    }

    /// <summary>Copy of a weapon data block (all public property-fields of the interop wrapper).</summary>
    private static ArchetypeDataBlock Clone(ArchetypeDataBlock src)
    {
        var dst = new ArchetypeDataBlock();
        foreach (var p in typeof(ArchetypeDataBlock).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            if (p.DeclaringType == typeof(Il2CppObjectBase) || p.DeclaringType == typeof(Il2CppSystem.Object)) continue;
            try { p.SetValue(dst, p.GetValue(src)); } catch { /* skip internal properties */ }
        }
        return dst;
    }

    private static void ShowMode(eWeaponFireMode mode)
    {
        string name = mode switch
        {
            eWeaponFireMode.Semi => Lang.T("SEMI", "ОДИНОЧНЫЙ"),
            eWeaponFireMode.Burst => Lang.T("BURST", "ОЧЕРЕДЬ"),
            _ => Lang.T("AUTO", "АВТО"),
        };
        var gui = GuiManager.InteractionLayer;
        gui.SetMessage(Lang.T("FIRE MODE: ", "РЕЖИМ ОГНЯ: ") + name, ePUIMessageStyle.Default, 10);
        gui.MessageVisible = true;
        _hideMsgAt = Time.time + 1.5f;
    }
}
