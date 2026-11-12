using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TCFModManager.ServerMap;

//
// The SPT profiles on this server, as SPT saves them: <server>\user\profiles\<id>.json. Read for
// /profile, so TCFModManager on another machine can show a profile's money and raid record.
//
public static partial class ServerProfiles
{
    public static string? FindDirectory()
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var candidate = Path.Combine(root, "user", "profiles");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    public static bool IsProfileId(string? id) => id is not null && ProfileId().IsMatch(id);

    [GeneratedRegex("^[0-9a-fA-F]{24}$")]
    private static partial Regex ProfileId();

    public static ProfileRead Read(string directory, string id, long? since)
    {
        var file = Path.Combine(directory, id + ".json");

        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return new ProfileRead(ProfileReadState.Missing, null, null);

            var written = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            if (since == written) return new ProfileRead(ProfileReadState.Unchanged, written, null);

            string text;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = reader.ReadToEnd();
            }

            using (var document = JsonDocument.Parse(text))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return new ProfileRead(ProfileReadState.Busy, null, null);
            }

            return new ProfileRead(ProfileReadState.Found, written, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ProfileRead(ProfileReadState.Busy, null, null);
        }
    }
}

public enum ProfileReadState
{
    Found,
    Unchanged,
    Missing,
    Busy,
}

public sealed record ProfileRead(ProfileReadState State, long? Written, string? Json);
