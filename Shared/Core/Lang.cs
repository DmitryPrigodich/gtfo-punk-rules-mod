using BepInEx.Configuration;

namespace GtfoPunk.Core;

/// <summary>
/// Language of the text the mod shows inside the game (HUD, loadout, terminal, summaries).
/// "auto" (default) follows the game's own language setting: Russian if the game is in Russian, otherwise English.
/// </summary>
public static class Lang
{
    private static ConfigEntry<string> _lang;

    internal static void Bind(ConfigFile config)
    {
        _lang = ConfigMigration.Bind(config, "General", "TextLanguage", "auto",
            "In-game text language of the mod: auto (as the game's language), en or ru.",
            ("General", "Language", "en"));
    }

    private static float _checkedAt = -100f;
    private static bool _gameRu;

    /// <summary>The game's language is Russian (checked at most once a second; false if unknown).</summary>
    private static bool GameRu()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - _checkedAt < 1f) return _gameRu;
        _checkedAt = now;
        try
        {
            var svc = Localization.Text.TextLocalizationService;
            var gd = svc == null ? null : ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)svc).TryCast<Localization.GameDataTextLocalizationService>();
            _gameRu = gd != null && gd.CurrentLanguage == Localization.Language.Russian;
        }
        catch { _gameRu = false; }
        return _gameRu;
    }

    public static bool Ru
    {
        get
        {
            string v = _lang?.Value?.Trim().ToLowerInvariant() ?? "auto";
            if (v.StartsWith("ru")) return true;
            if (v.StartsWith("en")) return false;
            return GameRu();
        }
    }

    /// <summary>Pick the string for the current language.</summary>
    public static string T(string en, string ru) => Ru ? ru : en;
}
