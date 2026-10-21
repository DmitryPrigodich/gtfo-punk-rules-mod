using System;
using HarmonyLib;

namespace GtfoPunk.Core;

/// <summary>
/// Общий покадровый «тик» для модулей (в лобби и в уровне), без своих MonoBehaviour.
/// Подписка: FrameTicker.OnUpdate += MyTick;  Исключения ловятся и не роняют остальных.
/// </summary>
public static class FrameTicker
{
    public static event Action OnUpdate;

    /// <summary>Вызывается при выходе из уровня (InLevel → любое другое), чтобы модули чистили состояние.</summary>
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
                // Отписываем сломанный обработчик, чтобы не спамить лог каждый кадр.
                if (name == "OnUpdate") OnUpdate -= handler;
            }
        }
    }
}
