using BepInEx;
using BepInEx.Unity.IL2CPP;
using GtfoPunk.Core;

namespace GtfoPunk.Rules;

/// <summary>
/// GtfoPunk — My Game My Rules: геймплейные изменения (баланс, новые предметы, правила).
/// Меняет игру — играть только с друзьями, у которых он тоже стоит.
/// </summary>
[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BasePlugin
{
    public const string GUID = "AmDDRed.GtfoPunk.Rules";
    public const string NAME = "GtfoPunk My Game My Rules";
    public const string VERSION = "0.3.0";

    public override void Load()
    {
        ModInfo.Start(GUID, NAME, VERSION, Config, Log);
        // После включения модулей (они подписываются на GearFactory.OnInject) — подключаем «гараж».
        IL2CPPChainloader.Instance.Finished += Core.GearFactory.Install;
    }
}
