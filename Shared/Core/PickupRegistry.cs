using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
using Player;
using UnityEngine;

namespace GtfoPunk.Core;

/// <summary>Реестр всех подбираемых предметов уровня (LG_PickupItem_Sync), чтобы не искать их по сцене.</summary>
public static class PickupRegistry
{
    private static readonly List<LG_PickupItem_Sync> _items = new();

    public static IReadOnlyList<LG_PickupItem_Sync> Items
    {
        get { Prune(); return _items; }
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(LG_PickupItem_Sync), nameof(LG_PickupItem_Sync.Setup)),
            postfix: new HarmonyMethod(typeof(PickupRegistry), nameof(OnSetup)));
        FrameTicker.OnLevelExit += () => _items.Clear();
    }

    private static void OnSetup(LG_PickupItem_Sync __instance)
    {
        if (__instance != null) _items.Add(__instance);
    }

    private static void Prune() => _items.RemoveAll(x => x == null);

    public static bool IsPlacedInLevel(LG_PickupItem_Sync sync)
        => sync != null && sync.GetCurrentState().status == ePickupItemStatus.PlacedInLevel;

    public static InventorySlot SlotOf(LG_PickupItem_Sync sync)
    {
        var item = sync?.item;
        var db = item?.ItemDataBlock;
        return db == null ? InventorySlot.None : db.inventorySlot;
    }

    /// <summary>Есть ли лежащий в уровне предмет в радиусе от точки.</summary>
    public static bool AnyPlacedNear(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        foreach (var s in Items)
        {
            if (!IsPlacedInLevel(s)) continue;
            if ((s.transform.position - pos).sqrMagnitude <= r2) return true;
        }
        return false;
    }
}
