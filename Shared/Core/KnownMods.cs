namespace GtfoPunk.Core;

/// <summary>
/// Third-party mods we defer to instead of our own implementation.
/// Matched by GUID and by plugin name (case-insensitive).
/// Names checked against Thunderstore; GUIDs to be confirmed once the DLLs are inspected.
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
