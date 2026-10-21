using BepInEx.Configuration;

namespace GtfoPunk.Core;

/// <summary>
/// Language of the text the mod shows inside the game (HUD, loadout, terminal, summaries).
/// English by default: the game's fonts are guaranteed to support it.
/// Russian can be enabled with [General] Language = ru (if the game font has Cyrillic).
/// </summary>
public static class Lang
{
    private static ConfigEntry<string> _lang;

    internal static void Bind(ConfigFile config)
    {
        _lang = config.Bind("General", "Language", "en",
            "In-game text language of the mod: en or ru. If Russian letters show up as boxes, switch back to en.");
    }

    public static bool Ru => _lang != null && _lang.Value.Trim().ToLowerInvariant().StartsWith("ru");

    /// <summary>Pick the string for the current language.</summary>
    public static string T(string en, string ru) => Ru ? ru : en;
}
