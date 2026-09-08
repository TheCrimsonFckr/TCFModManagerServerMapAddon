using System.Security.Cryptography;
using System.Text;

namespace TCFModManager.ServerMap;

//
// The shared key that gates every route except the handshake.
//
// Why one exists: this mod answers on the SPT server's own port, and a Fika server is very often
// port-forwarded. Proven 2026-09-06 - a client on another machine fetched the published list over
// the server's external IP. Whatever else is true, the routes are reachable from the internet, so
// "only my friends know the address" was never the access control it looked like.
//
// A shared key is the proportionate answer, not a login: the operator already distributes an
// address and a port, and this is one more string in the same message. It is not a password - it
// identifies nobody and protects nothing on its own - so it is generated rather than chosen, and
// there is no way to set a weak one.
//
// /hello stays open. It is asked before the user has been given anything, it says only what this
// server is, and gating it would mean a client could not tell "wrong address" from "right address,
// no key" - which is the difference between a typo and a message to send the operator.
//
public static class ServerMapKey
{
    public const string HeaderName = "X-ServerMap-Key";

    public const string FileName = "servermap-key.txt";

    //
    // No I, L, O, U, 0 or 1. This gets read off a console and typed into a text box on another
    // machine, and every one of those is a transcription error waiting to happen. 24 characters of
    // a 26-symbol alphabet is about 112 bits, which is far past anything that matters here.
    //
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int KeyLength = 24;

    private const int GroupSize = 4;

    private static readonly object Gate = new();

    private static string? _key;
    private static string? _cachedPath;
    private static long _cachedLength;
    private static DateTime _cachedWrittenUtc;

    //
    // The key this server expects.
    //
    // THE FILE IS THE KEY. This reads it, and generates one only when there is no file to read -
    // which, on a server that has been started once, is never. An operator who has handed a key out
    // must be able to rely on it still being the key tomorrow; a key that rotated on its own would
    // lock out everyone they gave it to, silently, with no message that says why.
    //
    // Re-read whenever the file's size or timestamp moves, the same way PublishedModList watches the
    // list. That is what lets the operator rotate the key deliberately - from TCF Mod Manager's
    // Options, or by editing the file - and have it take effect without restarting the server. The
    // common path is one stat.
    //
    // There is deliberately no way to turn the key off from here: an operator who wants an open
    // server can say so in a decision made on purpose, not by deleting a file and not noticing.
    //
    public static string Current(string configDirectory)
    {
        var path = Path.Combine(configDirectory, FileName);

        FileInfo? info = null;
        try
        {
            var candidate = new FileInfo(path);
            if (candidate.Exists) info = candidate;
        }
        catch (IOException)
        {
            // Unreadable right now; the cached key below is still the right answer if we have one.
        }

        lock (Gate)
        {
            //
            // Cached only while the file it came from is unchanged. Holding it for the life of the
            // process regardless was the bug this replaced: the file and the running server could
            // disagree, and nothing said so.
            //
            if (_key is not null
                && info is not null
                && _cachedPath == path
                && _cachedLength == info.Length
                && _cachedWrittenUtc == info.LastWriteTimeUtc)
            {
                return _key;
            }

            if (info is not null && TryRead(path) is { } existing)
            {
                _cachedPath = path;
                _cachedLength = info.Length;
                _cachedWrittenUtc = info.LastWriteTimeUtc;
                return _key = existing;
            }

            //
            // No file, or one that has been emptied. If we already hold a key, keep it rather than
            // minting another: a file that vanished under a running server is a problem to notice,
            // not a reason to invalidate every key already handed out.
            //
            if (_key is not null) return _key;

            return Write(configDirectory, Generate());
        }
    }

    //
    // Replaces the key with a new one, deliberately. Nothing calls this on its own - it is what the
    // operator presses when a key has been shared too widely, and it invalidates every copy of the
    // old one, which is the entire point of pressing it.
    //
    public static string Rotate(string configDirectory)
    {
        lock (Gate)
        {
            return Write(configDirectory, Generate());
        }
    }

    // Caller holds Gate.
    private static string Write(string configDirectory, string key)
    {
        var path = Path.Combine(configDirectory, FileName);

        try
        {
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(path, key + Environment.NewLine, new UTF8Encoding(false));

            var written = new FileInfo(path);
            _cachedPath = path;
            _cachedLength = written.Length;
            _cachedWrittenUtc = written.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            //
            // Held in memory for this run rather than dropped. A server whose config folder is
            // read-only still refuses unauthenticated requests; it just forgets the key on restart,
            // which is visibly broken rather than quietly open.
            //
            _cachedPath = null;
        }

        return _key = key;
    }

    //
    // Whether a presented key is the expected one.
    //
    // Compared in constant time. A timing attack against a game server over the internet is not a
    // realistic threat, but the alternative costs nothing and this is the one place in the mod where
    // a comparison decides access.
    //
    public static bool Verify(string expected, string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented)) return false;

        var a = Encoding.UTF8.GetBytes(Normalize(expected));
        var b = Encoding.UTF8.GetBytes(Normalize(presented));

        // FixedTimeEquals returns false for differing lengths without comparing, which leaks only
        // the length - and the length is a published constant.
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    //
    // Dashes and case are presentation. The key is printed grouped for reading, and someone will
    // type it without the dashes or in lower case; neither should be a failed connection they have
    // no way to diagnose.
    //
    public static string Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";

        var builder = new StringBuilder(KeyLength);

        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    // 24 random characters, printed in groups of four.
    public static string Generate()
    {
        var builder = new StringBuilder(KeyLength + KeyLength / GroupSize);

        for (var i = 0; i < KeyLength; i++)
        {
            if (i > 0 && i % GroupSize == 0) builder.Append('-');

            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var contents = File.ReadAllText(path);

            // A file someone has emptied or filled with whitespace is treated as absent, so the next
            // request regenerates rather than locking every client out with a key of "".
            return string.IsNullOrWhiteSpace(Normalize(contents)) ? null : contents.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
