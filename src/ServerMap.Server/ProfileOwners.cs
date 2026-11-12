using System.Text.Json;

namespace TCFModManager.ServerMap;

//
// Which machine made which SPT profile, in Data\ServerMap\profile-owners.json beside the key: profile
// id to the client id of the machine that made it. A profile with no entry belongs to nobody - the
// host's - until the host gives it to a machine in TCF Mod Manager on the server machine, which
// writes this same file. Re-read whenever it changes on disk.
//
public sealed class ProfileOwners
{
    public const string FileName = "profile-owners.json";

    // How new a profile must be for the machine that made it to claim it.
    public static readonly TimeSpan ClaimWindow = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);
    private long _length = -1;
    private DateTime _written;

    public ProfileOwners(string configDirectory) => _path = Path.Combine(configDirectory, FileName);

    public IReadOnlyList<string> OwnedBy(string? clientId)
    {
        if (!ClientReport.IsClientId(clientId)) return [];

        lock (_gate)
        {
            Refresh();
            return _owners.Where(o => string.Equals(o.Value, clientId, StringComparison.OrdinalIgnoreCase))
                .Select(o => o.Key)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool IsOwner(string profileId, string? clientId)
    {
        if (!ClientReport.IsClientId(clientId)) return false;

        lock (_gate)
        {
            Refresh();
            return _owners.TryGetValue(profileId, out var owner) && string.Equals(owner, clientId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public ClaimOutcome Claim(string profileId, string clientId, DateTime? profileCreatedUtc, DateTimeOffset now)
    {
        lock (_gate)
        {
            Refresh();

            if (_owners.TryGetValue(profileId, out var owner))
            {
                return string.Equals(owner, clientId, StringComparison.OrdinalIgnoreCase)
                    ? ClaimOutcome.Claimed
                    : ClaimOutcome.OwnedByAnother;
            }

            if (profileCreatedUtc is not { } created) return ClaimOutcome.NoSuchProfile;
            if (now.UtcDateTime - created > ClaimWindow) return ClaimOutcome.TooOld;

            _owners[profileId] = clientId;
            return Save() ? ClaimOutcome.Claimed : ClaimOutcome.NotSaved;
        }
    }

    private void Refresh()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                _owners.Clear();
                _length = -1;
                return;
            }

            if (info.Length == _length && info.LastWriteTimeUtc == _written) return;

            var stored = JsonSerializer.Deserialize<StoredOwners>(File.ReadAllText(_path), Json);
            _owners = new Dictionary<string, string>(
                (stored?.Owners ?? []).Where(o => ServerProfiles.IsProfileId(o.Key) && ClientReport.IsClientId(o.Value)),
                StringComparer.OrdinalIgnoreCase);
            _length = info.Length;
            _written = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Mid-write or hand-edited into something unreadable: the last good read stands.
        }
    }

    private bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new StoredOwners { Owners = new(_owners) }, Json));
            File.Move(temp, _path, overwrite: true);

            var info = new FileInfo(_path);
            _length = info.Length;
            _written = info.LastWriteTimeUtc;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class StoredOwners
    {
        public int SchemaVersion { get; set; } = 1;

        public Dictionary<string, string> Owners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

public enum ClaimOutcome
{
    Claimed,
    OwnedByAnother,
    NoSuchProfile,
    TooOld,
    NotSaved,
}
