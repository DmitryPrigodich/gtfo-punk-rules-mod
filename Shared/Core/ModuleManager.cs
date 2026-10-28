using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace GtfoPunk.Core;

internal static class ModuleManager
{
    public static readonly List<ModuleBase> Modules = new();

    private static ConfigEntry<bool> _respectOtherMods;

    /// <summary>Step 1 (in Load): find modules and bind config. Patches are not applied yet.</summary>
    public static void Discover(ConfigFile config)
    {
        _respectOtherMods = config.Bind("General", "RespectOtherMods", true,
            "If a third-party mod with the same feature is installed, our module turns itself off.");

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(ModuleBase)) && !t.IsAbstract)
                     .OrderBy(t => t.Name))
        {
            var module = (ModuleBase)Activator.CreateInstance(type);
            module.BindConfig(config);
            Modules.Add(module);
        }
        ConfigMigration.DropAllOrphans(config); // removed modules and options
    }

    /// <summary>Step 2 (once all plugins are loaded): decide what to enable and apply patches.</summary>
    public static void EnableAll()
    {
        var installed = IL2CPPChainloader.Instance.Plugins.Values
            .Where(p => p.Metadata.GUID != ModInfo.Guid)
            .ToList();

        foreach (var m in Modules)
        {
            if (!m.EnabledEntry.Value)
            {
                m.State = ModuleState.DisabledByConfig;
                m.StateReason = "disabled in config";
                continue;
            }

            if (_respectOtherMods.Value)
            {
                var other = installed.FirstOrDefault(p => m.ReplacedBy.Any(key =>
                    string.Equals(p.Metadata.GUID, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Metadata.Name, key, StringComparison.OrdinalIgnoreCase)));
                if (other != null)
                {
                    m.State = ModuleState.ReplacedByOtherMod;
                    m.StateReason = $"already provided by {other.Metadata.Name} ({other.Metadata.GUID})";
                    continue;
                }
            }

            try
            {
                m.Enable();
                m.State = ModuleState.Active;
            }
            catch (Exception e)
            {
                m.State = ModuleState.Failed;
                m.StateReason = e.GetType().Name + ": " + e.Message;
                ModInfo.Log.LogError($"Module {m.Id} failed to enable:\n{e}");
            }
        }

        PrintSummary();
    }

    private static void PrintSummary()
    {
        ModInfo.Log.LogMessage($"===== {ModInfo.Name} {ModInfo.Version}: modules =====");
        foreach (var m in Modules)
        {
            var mark = m.State switch
            {
                ModuleState.Active => "[ ON ]",
                ModuleState.DisabledByConfig => "[ off]",
                ModuleState.ReplacedByOtherMod => "[skip]",
                ModuleState.Failed => "[FAIL]",
                _ => "[ ?? ]",
            };
            var reason = string.IsNullOrEmpty(m.StateReason) ? "" : $" — {m.StateReason}";
            string line = $"{mark} {m.Id,-24} ({m.Side}){reason}"; // (BepInEx's interpolated log handler has no alignment support)
            ModInfo.Log.LogMessage(line);
        }
    }

}
