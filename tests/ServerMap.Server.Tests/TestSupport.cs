using System.Text;
using System.Text.Json;
using TCFModManager.ServerMap.Contract;

// The payload, the key and the published list keep process-wide caches keyed on their file's path,
// so the tests run one at a time rather than racing each other through them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TCFModManager.ServerMap.Tests;

// A config folder of the test's own, removed afterwards.
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smtest-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// A clock the test moves by hand.
public sealed class TestClock(DateTimeOffset start)
{
    public DateTimeOffset Now { get; private set; } = start;

    public void Advance(TimeSpan by) => Now += by;

    public Func<DateTimeOffset> Func => () => Now;
}

public static class Fixture
{
    // JSON as content, not layout: the registry and the map write indented, which changes no value.
    public static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    public static string Compact(string json) => Compact(JsonDocument.Parse(json).RootElement);

    public static string PathOf(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => System.IO.File.ReadAllText(PathOf(name));
}

//
// Drives a real payload the way the stub does: a flattened request in, a formed response out. The
// headers are what the stub would hand over, so a test that wants a caller's address sets it the
// way StubHeaders would.
//
public sealed class PayloadDriver : IDisposable
{
    public const string Local = "192.168.1.20";
    public const string Outside = "203.0.113.9";

    private readonly TempDirectory _directory = new();

    public PayloadDriver(DateTimeOffset? start = null)
    {
        Clock = new TestClock(start ?? DateTimeOffset.Parse("2026-09-28T12:00:00Z"));
        Payload = new ServerMapPayload(_directory.Path, Clock.Func);
        Key = ServerMapKey.Current(_directory.Path);
    }

    public ServerMapPayload Payload { get; }

    public TestClock Clock { get; }

    public string Key { get; }

    public string ConfigDirectory => _directory.Path;

    public string File(string name) => _directory.File(name);

    public void Publish(string json) => System.IO.File.WriteAllText(File(PublishedModList.PreferredFileName), json);

    public void LanOnly(bool on) =>
        System.IO.File.WriteAllText(File(ServerMapSettings.FileName), $"{{ \"lanOnly\": {(on ? "true" : "false")} }}");

    public Reply Send(string method, string route, string? body = null, string? key = null, string? address = Local,
        params (string Name, string Value)[] headers)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (key is not null) all[ServerMapKey.HeaderName] = key;
        if (address is not null) all[PayloadHeaders.RemoteAddress] = address;
        foreach (var (name, value) in headers) all[name] = value;

        var request = new PayloadRequest(method, "/tcfservermap" + route, null, all,
            body is null ? [] : Encoding.UTF8.GetBytes(body));

        return new Reply(Payload.HandleAsync(request, CancellationToken.None).GetAwaiter().GetResult());
    }

    public Reply Get(string route, bool withKey = true, string? address = Local, params (string, string)[] headers) =>
        Send("GET", route, null, withKey ? Key : null, address, headers);

    public Reply Post(string route, string body, bool withKey = true, string? address = Local) =>
        Send("POST", route, body, withKey ? Key : null, address);

    public void Dispose() => _directory.Dispose();
}

public sealed class Reply(PayloadResponse response)
{
    public int Status => response.StatusCode;

    public string Body => response.Body;

    public string ContentType => response.ContentType;

    public JsonElement Json => JsonDocument.Parse(response.Body).RootElement.Clone();

    public override string ToString() => $"{Status} {Body}";
}
