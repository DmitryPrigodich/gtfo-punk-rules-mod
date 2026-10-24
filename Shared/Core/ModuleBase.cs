using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace GtfoPunk.Core;

/// <summary>
/// Base class of a feature. To add a new feature:
///   1. inherit from ModuleBase in the Modules folder;
///   2. fill in Id / Description / Side (and ReplacedBy if an external equivalent exists);
///   3. put Harmony patches into nested classes with [HarmonyPatch] — they are applied automatically.
/// Registration is automatic (reflection).
/// </summary>
public abstract class ModuleBase
{
    /// <summary>Unique ID, also the config section name. Latin letters, no spaces.</summary>
    public abstract string Id { get; }

    /// <summary>Description for the config.</summary>
    public abstract string Description { get; }

    public abstract ModuleSide Side { get; }

    public virtual bool EnabledByDefault => true;

    /// <summary>
    /// External mods that do the same thing (GUID or plugin name, case-insensitive).
    /// If such a mod is installed, this module is not enabled.
    /// </summary>
    public virtual IReadOnlyList<string> ReplacedBy => Array.Empty<string>();

    public ConfigEntry<bool> EnabledEntry { get; private set; }
    public ModuleState State { get; internal set; } = ModuleState.NotLoaded;
    public string StateReason { get; internal set; } = "";

    protected ConfigFile Config { get; private set; }
    protected BepInEx.Logging.ManualLogSource Log => ModInfo.Log;

    private Harmony _harmony;

    internal void BindConfig(ConfigFile config)
    {
        Config = config;
        var sideText = Side switch
        {
            ModuleSide.Client => "works on your side, no host needed",
            ModuleSide.Host => "needs the HOST: only has effect when you host",
            ModuleSide.Everyone => "must be installed by EVERY player",
            _ => "",
        };
        EnabledEntry = config.Bind(Id, "Enabled", EnabledByDefault, $"{Description}\n[{sideText}]");
        OnBindConfig();
    }

    /// <summary>The module declares its settings here via Bind(...).</summary>
    protected virtual void OnBindConfig() { }

    protected ConfigEntry<T> Bind<T>(string key, T defaultValue, string description)
        => Config.Bind(Id, key, defaultValue, description);

    internal void Enable()
    {
        _harmony = new Harmony($"{ModInfo.Guid}.{Id}");
        foreach (var t in GetType().GetNestedTypes(AccessTools.all)
                     .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), true).Any()))
        {
            _harmony.CreateClassProcessor(t).Patch();
        }
        OnEnable();
    }

    /// <summary>Extra initialization after patches are applied.</summary>
    protected virtual void OnEnable() { }
}

public enum ModuleState
{
    NotLoaded,
    Active,
    DisabledByConfig,
    ReplacedByOtherMod,
    Failed,
}
