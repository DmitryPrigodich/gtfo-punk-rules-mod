using System.Collections.Generic;
using HarmonyLib;
using LevelGeneration;
using Player;
using UnityEngine;

namespace GtfoPunk.Core;

/// <summary>Registry of all pickup items in the level (LG_PickupItem_Sync), so we don't have to search the scene.</summary>
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

    /// <summary>Is there an item lying in the level within the radius of the point.</summary>
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
