using BepInEx.Configuration;

namespace GtfoPunk.Core;

/// <summary>
/// Язык текста, который мод выводит в самой игре (HUD, лоадаут, терминал, итоги).
/// По умолчанию английский: шрифты игры гарантированно его поддерживают.
/// Русский можно включить в [General] Language = ru (если в шрифте игры есть кириллица).
/// </summary>
public static class Lang
{
    private static ConfigEntry<string> _lang;

    internal static void Bind(ConfigFile config)
    {
        _lang = config.Bind("General", "Language", "en",
            "Язык текста мода внутри игры: en или ru. Если вместо русских букв квадраты — верни en.");
    }

    public static bool Ru => _lang != null && _lang.Value.Trim().ToLowerInvariant().StartsWith("ru");

    /// <summary>Выбрать строку по текущему языку.</summary>
    public static string T(string en, string ru) => Ru ? ru : en;
}
