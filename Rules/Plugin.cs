using BepInEx;
using BepInEx.Unity.IL2CPP;
using GtfoPunk.Core;

namespace GtfoPunk.Rules;

/// <summary>
/// GtfoPunk — My Game My Rules: gameplay changes (balance, new items, rules).
/// Changes the game — play only with friends who have it installed too.
/// </summary>
[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BasePlugin
{
    public const string GUID = "AmDDRed.GtfoPunk.Rules";
    public const string NAME = "GtfoPunk My Game My Rules";
    public const string VERSION = "0.9.37";

    public override void Load()
    {
        // The garage moved to GtfoPunk Weapons (0.9.34): keep its old settings in this file for that mod to carry over.
        ConfigMigration.KeepSections.UnionWith(new[] { "Garage", "GarageGuns" });
        ModInfo.Start(GUID, NAME, VERSION, Config, Log);
        Watermark.Register($"GPUNK RULES v{VERSION}");
        // After modules are enabled (they subscribe to GearFactory.OnInject) — hook up the "garage".
        IL2CPPChainloader.Instance.Finished += Core.GearFactory.Install;
    }
}
