using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TCFModManager.ServerMap;

//
// The operator's settings for this server, in Data\ServerMap\servermap.json beside the key.
//
// Written by TCF Mod Manager's Options on the server machine, and fine to edit by hand. Re-read
// whenever its size or timestamp moves, so a change takes effect on the next request without a
// restart. A file that is missing or will not parse means every setting is at its default - which
// for LAN-only is off, the same as a server that has never been told about it.
//
public sealed record ServerMapSettings
{
    public const string FileName = "servermap.json";

    public static readonly ServerMapSettings Defaults = new();

    // Refuse every request whose address is not on this machine's network (LocalNetwork).
    public bool LanOnly { get; init; }

    private static readonly object Gate = new();
    private static string? _cachedPath;
    private static long _cachedLength = -1;
    private static DateTime _cachedWrittenUtc;
    private static ServerMapSettings _cached = Defaults;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static ServerMapSettings Current(string configDirectory)
    {
        var path = Path.Combine(configDirectory, FileName);

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Remember(path, -1, default, Defaults);

                if (path == _cachedPath && info.Length == _cachedLength && info.LastWriteTimeUtc == _cachedWrittenUtc)
                    return _cached;

                var read = JsonSerializer.Deserialize<ServerMapSettings>(File.ReadAllText(path), Json) ?? Defaults;
                return Remember(path, info.Length, info.LastWriteTimeUtc, read);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Mid-write, locked, or hand-edited into something that does not parse. The last
                // good read stands for a file we have read before; a file we never could read is
                // the defaults.
                return path == _cachedPath ? _cached : Defaults;
            }
        }
    }

    private static ServerMapSettings Remember(string path, long length, DateTime written, ServerMapSettings settings)
    {
        _cachedPath = path;
        _cachedLength = length;
        _cachedWrittenUtc = written;
        _cached = settings;
        return settings;
    }
}

//
// What "on this server's network" means for LAN-only: the machine itself, the private ranges a home
// or LAN hands out, link-local, and 100.64.0.0/10 - the range Tailscale gives its peers, so a group
// that plays over Tailscale is still local. ZeroTier already hands out private ranges.
//
// Decided on the address the server's socket saw, never on anything the request says about itself.
//
public static class LocalNetwork
{
    public static bool IsLocal(string? address) =>
        IPAddress.TryParse(address, out var parsed) && IsLocal(parsed);

    public static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC           // fc00::/7 unique local
                || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80); // fe80::/10 link-local
        }

        return false;
    }
}
