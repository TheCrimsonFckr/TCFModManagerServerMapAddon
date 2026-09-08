using System.Text.Json;

namespace TCFModManager.ServerMap;

// A published list as the payload sees it: the bytes to serve, and just enough read out of them to
// answer the handshake without parsing the whole thing on every probe.
public sealed record PublishedList
{
    public required string Path { get; init; }

    public required string Json { get; init; }

    public string? Name { get; init; }

    public int Revision { get; init; }

    public int EntryCount { get; init; }

    public string? SptVersion { get; init; }
}

//
// The list this server publishes: a `.tcfmodlist` file the operator drops into
// <SPT root>\TCFModManager\ServerMap\config\.
//
// The operator curates it in TCFModManager and exports it, so what gets served is what they MEANT
// their server to run - not whatever happens to be sitting in user\mods. That decision is why this
// class is a file reader and not a scanner: nothing here has to resolve a folder to a Forge id, and
// the payload keeps its dependency list at zero.
//
// The file is served verbatim. It is already the wire format TCFModManager reads, and re-encoding it
// here would mean two implementations of one format that could disagree.
//
public static class PublishedModList
{
    // Serve this one if it is there, whatever else is.
    public const string PreferredFileName = "published.tcfmodlist";

    public const string Extension = ".tcfmodlist";

    private static readonly object Gate = new();

    private static string? _cachedPath;
    private static long _cachedLength;
    private static DateTime _cachedWrittenUtc;
    private static PublishedList? _cached;

    //
    // Where the operator's list and this server's key live: <SPT root>\TCFModManager\Data\ServerMap\.
    //
    // Derived rather than searched for, because the shape is fixed by the loader: the payload is
    // always found at TCFModManager\ServerMap\payload\, so two levels up from it IS the
    // TCFModManager folder, whatever the SPT layout around it.
    //
    // It used to be ServerMap\config\, beside the payload. That protected it from a payload update
    // and from nothing else - the folder people actually replace is TCFModManager\, and ServerMap\
    // sat inside it looking like part of the mod rather than like data. Under Data\ it is in the one
    // folder every deploy already knows to keep, next to the app's settings, install manifest and
    // config backups.
    //
    // The legacy folder is still read when it is the only one there, so a server whose app has not
    // yet migrated it keeps serving the same list and expecting the same key.
    //
    public static string ConfigDirectory(string payloadDirectory)
    {
        var serverMapDirectory =
            Path.GetDirectoryName(payloadDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? payloadDirectory;

        var current = Path.GetDirectoryName(serverMapDirectory) is { } appDirectory
            ? Path.Combine(appDirectory, "Data", "ServerMap")
            : null;

        var legacy = Path.Combine(serverMapDirectory, "config");

        if (current is null) return legacy;

        try
        {
            if (!Directory.Exists(current) && Directory.Exists(legacy)) return legacy;
        }
        catch (IOException)
        {
            // Unreadable either way; the current location is still where new files belong.
        }

        return current;
    }

    //
    // Null when nothing is published, which is a normal state and not an error - a server can run
    // the mod and simply not offer a list.
    //
    // Re-read whenever the file's size or timestamp moves, so dropping in a new export takes effect
    // without restarting the server. The handshake calls this on every probe, so the common path is
    // one stat.
    //
    public static PublishedList? Current(string configDirectory)
    {
        var path = Find(configDirectory);

        if (path is null)
        {
            lock (Gate) { _cached = null; _cachedPath = null; }
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch (IOException)
        {
            return null;
        }

        lock (Gate)
        {
            if (_cached is not null
                && _cachedPath == path
                && _cachedLength == info.Length
                && _cachedWrittenUtc == info.LastWriteTimeUtc)
            {
                return _cached;
            }
        }

        var parsed = ReadFrom(path);

        lock (Gate)
        {
            _cached = parsed;
            _cachedPath = path;
            _cachedLength = info.Length;
            _cachedWrittenUtc = info.LastWriteTimeUtc;
        }

        return parsed;
    }

    //
    // Which file to serve.
    //
    // `published.tcfmodlist` always wins. Failing that, a folder holding exactly one `.tcfmodlist`
    // serves it - the operator exports "Fika night.tcfmodlist" from the app and dropping it in
    // should just work, without a rename step nobody would guess at. Several files and no preferred
    // name is genuinely ambiguous, so nothing is served rather than a guess: the handshake then says
    // there is no list, which is at least true and visible.
    //
    public static string? Find(string configDirectory)
    {
        try
        {
            if (!Directory.Exists(configDirectory)) return null;

            var preferred = Path.Combine(configDirectory, PreferredFileName);
            if (File.Exists(preferred)) return preferred;

            var candidates = Directory.GetFiles(configDirectory, "*" + Extension);

            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    //
    // Reads the header fields the handshake needs. Deliberately NOT a full parse into the list model
    // - the payload does not reference TCFModManager.Core, and a second implementation of the format
    // here is exactly what would drift from the real one. The client parses the file properly; this
    // only has to know enough to say "there is a list, and it is at revision N".
    //
    // A file that does not answer those questions is treated as absent. Serving something the client
    // will reject is worse than saying there is nothing.
    //
    public static PublishedList? ReadFrom(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            //
            // NO SCHEMA CEILING HERE, deliberately.
            //
            // An earlier version refused any file newer than the schema this build knew, and it was
            // wrong twice over. Once in practice: entry scope stamped every published list schema 3,
            // this ceiling was still 2, and a server silently stopped serving its own list - reported
            // as "the server does not have a published list", which is about as far from the cause as
            // a message can get.
            //
            // And once in principle. This class does not INTERPRET the list; it serves the bytes
            // verbatim and reads four header fields off the front. The app that fetches it already
            // refuses a schema it cannot read, and says so properly. Putting a second, cruder version
            // check on the server means two numbers in two codebases that must be bumped together -
            // and a mismatch fails closed, quietly, on the half nobody is looking at.
            //
            // The bar is "can this be read as a mod list document", nothing more.
            //
            if (Property(root, "list") is not { ValueKind: JsonValueKind.Object } list) return null;

            var entries = Property(list, "entries");

            // Name and revision are what the handshake reports and what a client compares against.
            // A document that carries neither is not one this server can describe, whatever it is.
            if (String(list, "name") is null && Number(list, "revision") is null) return null;

            return new PublishedList
            {
                Path = path,
                Json = json,
                Name = String(list, "name"),
                Revision = Number(list, "revision") ?? 1,
                EntryCount = entries is { ValueKind: JsonValueKind.Array } ? entries.Value.GetArrayLength() : 0,
                SptVersion = String(list, "sptVersion"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    //
    // Case-insensitive, because the casing is not this reader's to assume.
    //
    // JsonDocument.TryGetProperty is case-sensitive, and the file is written by TCFModManager's own
    // serializer, whose naming policy is a setting in a different codebase - it currently writes
    // PascalCase, and the first version of this class looked for camelCase and silently found
    // nothing, so every list published read as "no list". Matching either way costs one enumeration
    // of a handful of properties and removes a whole class of that failure.
    //
    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return null;
    }

    private static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetInt32() : null;
}
