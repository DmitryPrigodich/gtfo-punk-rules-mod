using System;
using HarmonyLib;

namespace GtfoPunk.Core;

/// <summary>
/// Shared per-frame tick for modules (in lobby and in level) without their own MonoBehaviours.
/// Subscribe: FrameTicker.OnUpdate += MyTick;  Exceptions are caught and don't break other handlers.
/// </summary>
public static class FrameTicker
{
    public static event Action OnUpdate;

    /// <summary>Raised when leaving a level (InLevel → anything else) so modules can reset their state.</summary>
    public static event Action OnLevelExit;

    private static eGameStateName _lastState;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(GameStateManager), nameof(GameStateManager.Update)),
            postfix: new HarmonyMethod(typeof(FrameTicker), nameof(Tick)));
    }

    private static void Tick()
    {
        var state = GameStateManager.CurrentStateName;
        if (_lastState == eGameStateName.InLevel && state != eGameStateName.InLevel)
            Invoke(OnLevelExit, "OnLevelExit");
        _lastState = state;

        Invoke(OnUpdate, "OnUpdate");
    }

    private static void Invoke(Action evt, string name)
    {
        if (evt == null) return;
        foreach (Action handler in evt.GetInvocationList())
        {
            try { handler(); }
            catch (Exception e)
            {
                ModInfo.Log.LogError($"[{name}] {handler.Method.DeclaringType?.Name}.{handler.Method.Name}: {e}");
                // Unsubscribe the broken handler so it doesn't spam the log every frame.
                if (name == "OnUpdate") OnUpdate -= handler;
            }
        }
    }
}
