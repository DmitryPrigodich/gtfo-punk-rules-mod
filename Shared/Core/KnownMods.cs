namespace GtfoPunk.Core;

/// <summary>
/// Third-party mods we defer to instead of our own implementation (see ModuleBase.ReplacedBy).
/// Matched by plugin GUID or plugin name, case-insensitive. Names as published on Thunderstore.
/// </summary>
public static class KnownMods
{
    public const string PingEverything = "PingEverything";
    public const string ResourceHelper = "ResourceHelper";
    public const string PacksHelper = "PacksHelper";
    public const string DropItem = "DropItem";
    public const string SkipIntro = "SkipIntro";
    public const string QoLFix = "QoLFix";
}
