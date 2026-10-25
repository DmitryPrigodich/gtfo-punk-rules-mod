using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SNetwork;

namespace GtfoPunk.Rules.Core;

/// <summary>
/// Tiny mod-to-mod messaging over the game's own network transport (no GTFO-API needed).
/// A packet is: FF FF 'G' 'P' 'N' 'K' type payload…  The first two bytes are where the game keeps
/// a replicator key; 0xFFFF is never used by the game, so our packets are told apart and swallowed
/// before the game's replication sees them.
/// Every player must have the mod: a player without it gets a packet the game can't parse.
/// </summary>
public static class Net
{
    private static readonly byte[] Magic = { 0xFF, 0xFF, (byte)'G', (byte)'P', (byte)'N', (byte)'K' };
    private const int Header = 7; // magic + type byte

    private static readonly Dictionary<byte, Action<ulong, byte[]>> _handlers = new();
    private static bool _installed;
    private static int _logSent, _logReceived;

    /// <summary>Message types (one byte). Keep values stable.</summary>
    public const byte LoadoutChoice = 1;
    public const byte AmmoFix = 2;
    public const byte Cough = 3;

    public static void On(byte type, Action<ulong, byte[]> handler)
    {
        Install();
        _handlers[type] = handler;
    }

    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var target = AccessTools.Method(typeof(SNet_Replication), nameof(SNet_Replication.RecieveBytes),
                new[] { typeof(Il2CppStructArray<byte>), typeof(uint), typeof(ulong) });
            new Harmony(ModInfo.Guid + ".Net").Patch(target, prefix: new HarmonyMethod(typeof(Net), nameof(ReceivePrefix)));
            ModInfo.Log.LogInfo("[Net] message channel ready");
        }
        catch (Exception e) { ModInfo.Log.LogError($"[Net] can't hook network receive: {e}"); }
    }

    /// <summary>Send to everyone else in the lobby / game.</summary>
    public static void SendToAll(byte type, byte[] payload)
    {
        if (SNet.Core == null || !SNet.HasLocalPlayer) return;
        var bytes = new byte[Header + payload.Length];
        Array.Copy(Magic, bytes, Magic.Length);
        bytes[Magic.Length] = type;
        Array.Copy(payload, 0, bytes, Header, payload.Length);
        try
        {
            SNet.Core.SendBytes(new Il2CppStructArray<byte>(bytes), SNet_SendGroup.PlayersInSessionHub, SNet_SendQuality.Reliable, (int)SNet_ChannelType.GameOrderCritical);
            if (_logSent++ < 5) ModInfo.Log.LogInfo($"[Net] sent type {type}, {payload.Length} bytes");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Net] send failed: {e.Message}"); }
    }

    /// <summary>Send to one player.</summary>
    public static void SendTo(SNet_Player to, byte type, byte[] payload)
    {
        if (SNet.Core == null || to == null) return;
        var bytes = new byte[Header + payload.Length];
        Array.Copy(Magic, bytes, Magic.Length);
        bytes[Magic.Length] = type;
        Array.Copy(payload, 0, bytes, Header, payload.Length);
        try
        {
            SNet.Core.SendBytes(new Il2CppStructArray<byte>(bytes), SNet_SendQuality.Reliable, (int)SNet_ChannelType.GameOrderCritical, to);
            if (_logSent++ < 20) ModInfo.Log.LogInfo($"[Net] sent type {type} to {to.NickName}, {payload.Length} bytes");
        }
        catch (Exception e) { ModInfo.Log.LogWarning($"[Net] send to {to.NickName} failed: {e.Message}"); }
    }

    private static bool ReceivePrefix(Il2CppStructArray<byte> bytes, uint size, ulong messagerID)
    {
        try
        {
            if (bytes == null || size < Header || bytes[0] != 0xFF || bytes[1] != 0xFF) return true;
            for (int i = 2; i < Magic.Length; i++) if (bytes[i] != Magic[i]) return true;
            byte type = bytes[Magic.Length];
            var payload = new byte[size - Header];
            for (int i = 0; i < payload.Length; i++) payload[i] = bytes[Header + i];
            if (_logReceived++ < 10) ModInfo.Log.LogInfo($"[Net] received type {type}, {payload.Length} bytes from {messagerID}");
            if (_handlers.TryGetValue(type, out var h)) h(messagerID, payload);
            else ModInfo.Log.LogWarning($"[Net] unknown message type {type} (other player has a different mod version?)");
            return false;
        }
        catch (Exception e)
        {
            ModInfo.Log.LogWarning($"[Net] receive: {e.Message}");
            return true;
        }
    }
}
