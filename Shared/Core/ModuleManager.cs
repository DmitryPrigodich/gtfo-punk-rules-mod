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

    /// <summary>Шаг 1 (в Load): находим модули и биндим конфиг. Патчи ещё не ставим.</summary>
    public static void Discover(ConfigFile config)
    {
        _respectOtherMods = config.Bind("General", "RespectOtherMods", true,
            "Если установлен сторонний мод с той же функцией, наш модуль автоматически выключается.");

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(ModuleBase)) && !t.IsAbstract)
                     .OrderBy(t => t.Name))
        {
            var module = (ModuleBase)Activator.CreateInstance(type);
            module.BindConfig(config);
            Modules.Add(module);
        }
    }

    /// <summary>Шаг 2 (когда все плагины загружены): решаем, что включать, и ставим патчи.</summary>
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
                m.StateReason = "выключен в конфиге";
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
                    m.StateReason = $"уже есть {other.Metadata.Name} ({other.Metadata.GUID})";
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
                ModInfo.Log.LogError($"Модуль {m.Id} упал при включении:\n{e}");
            }
        }

        PrintSummary();
    }

    private static void PrintSummary()
    {
        ModInfo.Log.LogMessage($"===== {ModInfo.Name} {ModInfo.Version}: модули =====");
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
            string line = $"{mark} {m.Id,-24} ({m.Side}){reason}";
            ModInfo.Log.LogMessage(line);
        }
    }

    public static string ShortStatus()
    {
        int on = Modules.Count(m => m.State == ModuleState.Active);
        int skip = Modules.Count(m => m.State == ModuleState.ReplacedByOtherMod);
        int fail = Modules.Count(m => m.State == ModuleState.Failed);
        return $"{on}/{Modules.Count} on, {skip} replaced by other mods, {fail} failed";
    }
}
