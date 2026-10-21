using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace GtfoPunk.Core;

/// <summary>
/// Info about the concrete plugin (QoL or Rules) the shared core is compiled into.
/// Each package carries its own copy of the core, so their statics are separate.
/// </summary>
public static class ModInfo
{
    public static string Guid { get; private set; } = "";
    public static string Name { get; private set; } = "";
    public static string Version { get; private set; } = "";
    public static ManualLogSource Log { get; private set; }

    /// <summary>Called from the plugin's Load(): sets up infrastructure, finds modules, enables them once all plugins are loaded.</summary>
    public static void Start(string guid, string name, string version, ConfigFile config, ManualLogSource log)
    {
        Guid = guid; Name = name; Version = version; Log = log;

        var core = new HarmonyLib.Harmony(guid + ".Core");
        FrameTicker.Install(core);
        PickupRegistry.Install(core);

        Lang.Bind(config);
        ModuleManager.Discover(config);
        IL2CPPChainloader.Instance.Finished += ModuleManager.EnableAll;
        Log.LogInfo($"{name} {version} loaded, modules found: {ModuleManager.Modules.Count}");
    }
}
