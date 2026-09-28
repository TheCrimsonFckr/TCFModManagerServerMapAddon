using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.ServerMap;

//
// One machine running TCFModManager that has agreed to report to this server.
//
// Mods is kept as the JSON the app sent and never read here. The payload does not interpret the
// inventory any more than it interprets the published list: the app writes it and the app reads it
// back, so there is one implementation of its shape, not two that can disagree.
//
public sealed class ClientRecord
{
    public string ClientId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Hosts { get; set; }

    public bool Plays { get; set; }

    public bool Headless { get; set; }

    public bool GameRunning { get; set; }

    public string? SptVersion { get; set; }

    public string? AppVersion { get; set; }

    public string? InventoryHash { get; set; }

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public DateTimeOffset? InventoryReportedAt { get; set; }

    public JsonElement? Mods { get; set; }
}

public sealed record ReportOutcome(bool Accepted, bool Resend, string? Error);

//
// Every machine that reports to this server, kept in Data\ServerMap\clients.json beside the key and
// the published list, so it survives both a payload update and a server restart.
//
// A heartbeat that changes nothing but LastSeen is not written straight away. Presence is only
// worth anything while the server is up, and rewriting every inventory once a minute per machine
// buys nothing a restart would miss; anything that DOES change what the map shows is written at once.
//
public sealed class ClientRegistry
{
    public const string FileName = "clients.json";

    public const int IntervalSeconds = 60;

    public const int MaxClients = 64;

    public const int MaxDisplayNameLength = 64;

    public const int MaxMods = 4000;

    public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(30);

    private static readonly TimeSpan HeartbeatSaveInterval = TimeSpan.FromMinutes(5);

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, ClientRecord> _clients = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastSaved;
    private bool _dirty;

    public ClientRegistry(string configDirectory, Func<DateTimeOffset>? now = null)
    {
        _path = Path.Combine(configDirectory, FileName);
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public string FilePath => _path;

    public ReportOutcome Report(ClientReport report)
    {
        var problem = report.Problem();
        if (problem is not null) return new ReportOutcome(false, false, problem);

        var now = _now();

        lock (_gate)
        {
            PruneLocked(now);

            var id = report.ClientId!;
            var isNew = !_clients.TryGetValue(id, out var record);

            if (isNew)
            {
                if (_clients.Count >= MaxClients) EvictOldestLocked();
                record = new ClientRecord { ClientId = id, FirstSeen = now };
                _clients[id] = record;
            }

            var shown = Describe(record!);

            record!.DisplayName = report.CleanDisplayName();
            record.Hosts = report.Hosts;
            record.Plays = report.Plays;
            record.Headless = report.Headless;
            record.GameRunning = report.GameRunning;
            record.SptVersion = Trim(report.SptVersion, 32);
            record.AppVersion = Trim(report.AppVersion, 32);
            record.LastSeen = now;

            var resend = false;

            if (report.Mods is { } mods)
            {
                record.Mods = mods.Clone();
                record.InventoryHash = report.InventoryHash;
                record.InventoryReportedAt = now;
            }
            else if (record.Mods is null
                     || !string.Equals(record.InventoryHash, report.InventoryHash, StringComparison.Ordinal))
            {
                resend = true;
            }

            var changed = isNew || report.Mods is not null || shown != Describe(record);

            if (changed || now - _lastSaved >= HeartbeatSaveInterval) SaveLocked(now);
            else _dirty = true;

            return new ReportOutcome(true, resend, null);
        }
    }

    public bool Withdraw(string? clientId)
    {
        if (!ClientReport.IsClientId(clientId)) return false;

        lock (_gate)
        {
            if (!_clients.Remove(clientId!)) return false;
            SaveLocked(_now());
            return true;
        }
    }

    public IReadOnlyList<ClientRecord> Snapshot()
    {
        lock (_gate)
        {
            var now = _now();
            if (PruneLocked(now)) SaveLocked(now);

            return _clients.Values
                .OrderByDescending(c => c.Hosts)
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_dirty) SaveLocked(_now());
        }
    }

    private static string Describe(ClientRecord c) =>
        $"{c.DisplayName}|{c.Hosts}|{c.Plays}|{c.Headless}|{c.GameRunning}|{c.SptVersion}|{c.AppVersion}";

    private bool PruneLocked(DateTimeOffset now)
    {
        var stale = _clients.Values.Where(c => now - c.LastSeen > ForgetAfter).Select(c => c.ClientId).ToList();
        foreach (var id in stale) _clients.Remove(id);
        return stale.Count > 0;
    }

    private void EvictOldestLocked()
    {
        var oldest = _clients.Values.OrderBy(c => c.LastSeen).FirstOrDefault();
        if (oldest is not null) _clients.Remove(oldest.ClientId);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var stored = JsonSerializer.Deserialize<StoredRegistry>(File.ReadAllText(_path), Json);
            if (stored?.Clients is null) return;

            foreach (var client in stored.Clients)
            {
                if (ClientReport.IsClientId(client.ClientId)) _clients[client.ClientId] = client;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            //
            // A registry that cannot be read starts empty. Every machine that is still around
            // reports again within a minute, so nothing is lost that will not come straight back -
            // and refusing to serve over a corrupt state file would take the list down with it.
            //
            _clients.Clear();
        }
    }

    private void SaveLocked(DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var temp = _path + ".tmp";
            var stored = new StoredRegistry { Clients = _clients.Values.ToList() };
            File.WriteAllText(temp, JsonSerializer.Serialize(stored, Json));
            File.Move(temp, _path, overwrite: true);

            _lastSaved = now;
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dirty = true;
        }
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private sealed class StoredRegistry
    {
        public int SchemaVersion { get; set; } = 1;

        public List<ClientRecord> Clients { get; set; } = new();
    }
}

//
// What a machine sends on POST /report. Mods is absent on an ordinary heartbeat and present when the
// inventory changed or the server asked for it with resend.
//
public sealed class ClientReport
{
    public int Protocol { get; set; }

    public string? ClientId { get; set; }

    public string? DisplayName { get; set; }

    public bool Hosts { get; set; }

    public bool Plays { get; set; }

    public bool Headless { get; set; }

    public bool GameRunning { get; set; }

    public string? SptVersion { get; set; }

    public string? AppVersion { get; set; }

    public string? InventoryHash { get; set; }

    public JsonElement? Mods { get; set; }

    public static bool IsClientId(string? value) => Guid.TryParseExact(value, "D", out _);

    public string? Problem()
    {
        if (!IsClientId(ClientId)) return "clientId must be a GUID.";
        if (string.IsNullOrWhiteSpace(InventoryHash) || InventoryHash.Length > 128)
            return "inventoryHash is required.";

        if (Mods is { } mods)
        {
            if (mods.ValueKind != JsonValueKind.Array) return "mods must be an array.";
            if (mods.GetArrayLength() > ClientRegistry.MaxMods) return "Too many mods in one report.";
        }

        return null;
    }

    public string CleanDisplayName()
    {
        var name = new string((DisplayName ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) return "Unnamed machine";
        return name.Length <= ClientRegistry.MaxDisplayNameLength ? name : name[..ClientRegistry.MaxDisplayNameLength];
    }
}
