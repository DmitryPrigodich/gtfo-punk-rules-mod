namespace GtfoPunk.Core;

/// <summary>Where the module has to run to have an effect.</summary>
public enum ModuleSide
{
    /// <summary>Local only: works for any player, no host needed.</summary>
    Client,

    /// <summary>Needs the host: only has effect when you host (or the host has the mod too).</summary>
    Host,

    /// <summary>Must be installed by everyone, otherwise things desync.</summary>
    Everyone,
}
