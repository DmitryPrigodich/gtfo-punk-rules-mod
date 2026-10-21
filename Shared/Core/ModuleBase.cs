using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using SNetwork;

namespace GtfoPunk.Core;

/// <summary>
/// Базовый класс фичи. Чтобы добавить новую фичу:
///   1. унаследуйся от ModuleBase в папке Modules;
///   2. заполни Id / Description / Side (и ReplacedBy, если есть внешний аналог);
///   3. положи Harmony-патчи вложенными классами с [HarmonyPatch] — они применятся сами.
/// Регистрация автоматическая (рефлексией).
/// </summary>
public abstract class ModuleBase
{
    /// <summary>Уникальный ID, он же имя секции в конфиге. Латиница, без пробелов.</summary>
    public abstract string Id { get; }

    /// <summary>Описание для конфига.</summary>
    public abstract string Description { get; }

    public abstract ModuleSide Side { get; }

    public virtual bool EnabledByDefault => true;

    /// <summary>
    /// Внешние моды, которые делают то же самое (GUID или имя плагина, без учёта регистра).
    /// Если такой мод установлен, модуль не включается.
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
            ModuleSide.Client => "работает у себя, хост не нужен",
            ModuleSide.Host => "нужен ХОСТ: эффект только когда хостишь ты",
            ModuleSide.Everyone => "должен стоять У ВСЕХ игроков",
            _ => "",
        };
        EnabledEntry = config.Bind(Id, "Enabled", EnabledByDefault, $"{Description}\n[{sideText}]");
        OnBindConfig();
    }

    /// <summary>Здесь модуль объявляет свои настройки через Bind(...).</summary>
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

    /// <summary>Доп. инициализация после применения патчей.</summary>
    protected virtual void OnEnable() { }

    /// <summary>
    /// Проверка в рантайме для Host-модулей: true, если мы сейчас хост.
    /// Client/Everyone всегда true.
    /// </summary>
    public bool ShouldRunNow => Side != ModuleSide.Host || (SNet.HasMaster && SNet.IsMaster);
}

public enum ModuleState
{
    NotLoaded,
    Active,
    DisabledByConfig,
    ReplacedByOtherMod,
    Failed,
}
