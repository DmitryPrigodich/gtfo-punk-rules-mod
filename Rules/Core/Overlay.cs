using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace GtfoPunk.Rules.Core;

/// <summary>Unity calls OnGUI on this component every frame; it forwards the repaint to <see cref="Overlay.OnDraw"/>.</summary>
public class OverlayBehaviour : MonoBehaviour
{
    public OverlayBehaviour(IntPtr ptr) : base(ptr) { }

    public void OnGUI() => Overlay.Draw();
}

/// <summary>
/// Screen-space drawing (immediate mode GUI) for HUD elements that aren't text: subscribe to OnDraw and call
/// GUI.DrawTexture there. Created on first use; survives level changes.
/// </summary>
public static class Overlay
{
    public static event Action OnDraw;

    private static GameObject _host;
    private static bool _failed;

    public static void Ensure()
    {
        if (_host != null || _failed) return;
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<OverlayBehaviour>()) ClassInjector.RegisterTypeInIl2Cpp<OverlayBehaviour>();
            _host = new GameObject("GtfoPunk_Overlay");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;
            _host.AddComponent<OverlayBehaviour>();
            ModInfo.Log.LogInfo("[Overlay] screen overlay ready");
        }
        catch (Exception e) { _failed = true; ModInfo.Log.LogError($"[Overlay] can't create the screen overlay: {e}"); }
    }

    internal static void Draw()
    {
        var ev = Event.current;
        if (OnDraw == null || ev == null || ev.type != EventType.Repaint) return;
        foreach (Action h in OnDraw.GetInvocationList())
        {
            try { h(); }
            catch (Exception e) { ModInfo.Log.LogError($"[Overlay] {h.Method.DeclaringType?.Name}: {e.Message}"); OnDraw -= h; }
        }
    }
}
