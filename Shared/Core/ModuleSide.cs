namespace GtfoPunk.Core;

/// <summary>Где модуль должен работать, чтобы дать эффект.</summary>
public enum ModuleSide
{
    /// <summary>Только у себя: работает у любого игрока, хост не нужен.</summary>
    Client,

    /// <summary>Нужен хост: эффект есть, только когда хостишь ты (или хост тоже с модом).</summary>
    Host,

    /// <summary>Должен стоять у всех участников, иначе рассинхрон.</summary>
    Everyone,
}
