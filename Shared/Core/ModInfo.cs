using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace GtfoPunk.Core;

/// <summary>
/// Сведения о конкретном плагине (QoL или Rules), в который скомпилировано общее ядро.
/// Каждый пакет содержит свою копию ядра, поэтому статика у них раздельная.
/// </summary>
public static class ModInfo
{
    public static string Guid { get; private set; } = "";
    public static string Name { get; private set; } = "";
    public static string Version { get; private set; } = "";
    public static ManualLogSource Log { get; private set; }

    /// <summary>Вызывается из Load() плагина: ставит инфраструктуру, находит модули, включает их после загрузки всех плагинов.</summary>
    public static void Start(string guid, string name, string version, ConfigFile config, ManualLogSource log)
    {
        Guid = guid; Name = name; Version = version; Log = log;

        var core = new HarmonyLib.Harmony(guid + ".Core");
        FrameTicker.Install(core);
        PickupRegistry.Install(core);

        Lang.Bind(config);
        ModuleManager.Discover(config);
        IL2CPPChainloader.Instance.Finished += ModuleManager.EnableAll;
        Log.LogInfo($"{name} {version} загружен, найдено модулей: {ModuleManager.Modules.Count}");
    }
}
