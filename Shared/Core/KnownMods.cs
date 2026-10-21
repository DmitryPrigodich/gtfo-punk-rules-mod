namespace GtfoPunk.Core;

/// <summary>
/// Сторонние моды, на которые мы опираемся вместо своих реализаций.
/// Сравнение идёт и по GUID, и по имени плагина (без учёта регистра).
/// Имена сверены с Thunderstore; GUID уточним, когда скачаем DLL.
/// </summary>
public static class KnownMods
{
    public const string EnemyAnimationFix = "EnemyAnimationFix";
    public const string MemoryLeakFix = "MemoryLeakFix";
    public const string KillIndicatorFix = "KillIndicatorFix";
    public const string DamageSync = "DamageSync";
    public const string BetterBots = "BetterBots";
    public const string PingEverything = "PingEverything";
    public const string ResourceHelper = "ResourceHelper";
    public const string PacksHelper = "PacksHelper";
    public const string AccurateCrosshair = "AccurateCrosshair";
    public const string DropItem = "DropItem";
    public const string SkipIntro = "SkipIntro";
    public const string QoLFix = "QoLFix";
}
