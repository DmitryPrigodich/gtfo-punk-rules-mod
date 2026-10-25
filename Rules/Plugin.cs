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
    public const string VERSION = "0.9.4";

    public override void Load()
    {
        ModInfo.Start(GUID, NAME, VERSION, Config, Log);
        // After modules are enabled (they subscribe to GearFactory.OnInject) — hook up the "garage".
        IL2CPPChainloader.Instance.Finished += Core.GearFactory.Install;
    }
}
