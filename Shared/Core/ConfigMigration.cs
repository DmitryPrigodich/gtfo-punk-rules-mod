using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace GtfoPunk.Core;

/// <summary>
/// Renaming / moving config keys without losing what players set. BepInEx keeps unknown keys of the file as
/// "orphaned entries" (private); we read them to carry old values over and to drop keys that are no longer used.
/// </summary>
public static class ConfigMigration
{
    private static Dictionary<ConfigDefinition, string> Orphans(ConfigFile cfg)
    {
        try { return AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(cfg) as Dictionary<ConfigDefinition, string>; }
        catch { return null; }
    }

    public static ConfigEntry<T> Bind<T>(ConfigFile cfg, string section, string key, T defaultValue, string description,
        params (string Section, string Key, string OldDefault)[] old)
    {
        var orphans = Orphans(cfg);
        var def = new ConfigDefinition(section, key);
        bool existed = orphans != null && orphans.ContainsKey(def);
        var entry = cfg.Bind(def, defaultValue, new ConfigDescription(description));
        if (orphans == null) return entry;
        foreach (var (s, k, oldDefault) in old)
        {
            var od = new ConfigDefinition(s, k);
            if (!orphans.TryGetValue(od, out var value)) continue;
            if (!existed && (oldDefault == null || !string.Equals(value.Trim(), oldDefault, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    entry.SetSerializedValue(value);
                    existed = true; // take the first old value only
                    ModInfo.Log.LogInfo($"[Config] {s}.{k} = {value} → {section}.{key}");
                }
                catch (Exception e) { ModInfo.Log.LogWarning($"[Config] can't carry {s}.{k} over: {e.Message}"); }
            }
            orphans.Remove(od);
        }
        return entry;
    }

    /// <summary>Drops every leftover (unbound) key of a section from the file.</summary>
    public static int DropOrphans(ConfigFile cfg, string section)
    {
        var orphans = Orphans(cfg);
        if (orphans == null) return 0;
        var dead = orphans.Keys.Where(d => d.Section == section).ToList();
        foreach (var d in dead) orphans.Remove(d);
        if (dead.Count > 0) ModInfo.Log.LogInfo($"[Config] removed {dead.Count} unused keys from [{section}]: {string.Join(", ", dead.Select(d => d.Key))}");
        return dead.Count;
    }
}
