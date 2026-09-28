using System.Text.Json;

namespace TCFModManager.ServerMap.Tests;

public class ClientRegistryTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-09-28T12:00:00Z"));
    private readonly string _id = Guid.NewGuid().ToString();

    public void Dispose() => _dir.Dispose();

    private ClientRegistry Registry() => new(_dir.Path, _clock.Func);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private ClientReport Report(string? mods = null, string hash = "h1", string? id = null, string name = "Box A",
        bool hosts = false, bool gameRunning = false) => new()
    {
        Protocol = 1,
        ClientId = id ?? _id,
        DisplayName = name,
        Hosts = hosts,
        Plays = !hosts,
        GameRunning = gameRunning,
        InventoryHash = hash,
        Mods = mods is null ? null : Json(mods),
    };

    [Fact]
    public void A_new_machine_without_its_mods_is_asked_for_them()
    {
        var outcome = Registry().Report(Report());

        Assert.True(outcome.Accepted);
        Assert.True(outcome.Resend);
    }

    [Fact]
    public void Mods_once_sent_are_not_asked_for_again_while_the_hash_holds()
    {
        var registry = Registry();
        registry.Report(Report("[{\"modId\":5}]"));

        Assert.False(registry.Report(Report()).Resend);
        Assert.True(registry.Report(Report(hash: "h2")).Resend);
    }

    [Theory]
    [InlineData("not-a-guid", "h1", null)]
    [InlineData(null, "h1", null)]
    [InlineData("VALID", "", null)]
    [InlineData("VALID", "h1", "{}")]
    public void A_malformed_report_is_refused(string? id, string hash, string? mods)
    {
        var report = Report(mods, hash, id == "VALID" ? _id : id ?? "");
        if (id is null) report.ClientId = null;

        var outcome = Registry().Report(report);

        Assert.False(outcome.Accepted);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void Too_many_mods_is_refused()
    {
        var mods = "[" + string.Join(",", Enumerable.Repeat("{}", ClientRegistry.MaxMods + 1)) + "]";

        Assert.False(Registry().Report(Report(mods)).Accepted);
    }

    [Fact]
    public void Names_lose_control_characters_and_are_capped()
    {
        var registry = Registry();
        registry.Report(Report(name: "Box\u0007 A\n"));
        registry.Report(Report(id: Guid.NewGuid().ToString(), name: new string('x', 200)));
        registry.Report(Report(id: Guid.NewGuid().ToString(), name: "\u0001 "));

        var names = registry.Snapshot().Select(c => c.DisplayName).ToList();

        Assert.Contains("Box A", names);
        Assert.Contains(new string('x', ClientRegistry.MaxDisplayNameLength), names);
        Assert.Contains("Unnamed machine", names);
    }

    [Fact]
    public void Mods_survive_a_restart_verbatim_in_a_camel_case_file()
    {
        Registry().Report(Report("[{\"modId\":5,\"name\":\"SAIN\",\"somethingNew\":[1,2]}]"));

        var reloaded = Registry().Snapshot().Single();

        Assert.Equal("[{\"modId\":5,\"name\":\"SAIN\",\"somethingNew\":[1,2]}]", Fixture.Compact(reloaded.Mods!.Value));
        Assert.Contains("\"displayName\"", File.ReadAllText(Registry().FilePath));
    }

    [Fact]
    public void A_heartbeat_that_changes_nothing_is_written_at_most_every_five_minutes()
    {
        var registry = Registry();
        registry.Report(Report("[]"));
        var written = File.ReadAllText(registry.FilePath);

        _clock.Advance(TimeSpan.FromMinutes(1));
        registry.Report(Report());
        Assert.Equal(written, File.ReadAllText(registry.FilePath));

        _clock.Advance(TimeSpan.FromMinutes(5));
        registry.Report(Report());
        Assert.NotEqual(written, File.ReadAllText(registry.FilePath));
    }

    [Fact]
    public void Flush_writes_a_heartbeat_held_back()
    {
        var registry = Registry();
        registry.Report(Report("[]"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        registry.Report(Report());
        registry.Flush();

        Assert.Equal(_clock.Now, Registry().Snapshot().Single().LastSeen);
    }

    [Fact]
    public void Something_shown_on_the_map_changing_is_written_straight_away()
    {
        var registry = Registry();
        registry.Report(Report("[]"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        registry.Report(Report(gameRunning: true));

        Assert.True(Registry().Snapshot().Single().GameRunning);
    }

    [Fact]
    public void A_machine_not_heard_from_for_thirty_days_is_forgotten()
    {
        var registry = Registry();
        registry.Report(Report("[]"));

        _clock.Advance(ClientRegistry.ForgetAfter + TimeSpan.FromMinutes(1));

        Assert.Empty(registry.Snapshot());
        Assert.Empty(Registry().Snapshot());
    }

    [Fact]
    public void The_sixty_fifth_machine_pushes_out_the_one_heard_from_longest_ago()
    {
        var registry = Registry();
        var first = Guid.NewGuid().ToString();
        registry.Report(Report("[]", id: first, name: "first"));

        for (var i = 1; i < ClientRegistry.MaxClients; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            registry.Report(Report("[]", id: Guid.NewGuid().ToString(), name: $"m{i}"));
        }

        _clock.Advance(TimeSpan.FromSeconds(1));
        registry.Report(Report("[]", id: Guid.NewGuid().ToString(), name: "late"));

        var names = registry.Snapshot().Select(c => c.DisplayName).ToList();
        Assert.Equal(ClientRegistry.MaxClients, names.Count);
        Assert.DoesNotContain("first", names);
        Assert.Contains("late", names);
    }

    [Fact]
    public void Withdraw_removes_the_machine_and_saves()
    {
        var registry = Registry();
        registry.Report(Report("[]"));

        Assert.True(registry.Withdraw(_id.ToUpperInvariant()));
        Assert.Empty(Registry().Snapshot());
        Assert.False(registry.Withdraw(_id));
        Assert.False(registry.Withdraw("not-a-guid"));
        Assert.False(registry.Withdraw(null));
    }

    [Fact]
    public void The_host_comes_first_then_by_name()
    {
        var registry = Registry();
        registry.Report(Report("[]", id: Guid.NewGuid().ToString(), name: "bravo"));
        registry.Report(Report("[]", id: Guid.NewGuid().ToString(), name: "Zulu server", hosts: true));
        registry.Report(Report("[]", id: Guid.NewGuid().ToString(), name: "alpha"));

        Assert.Equal(["Zulu server", "alpha", "bravo"], registry.Snapshot().Select(c => c.DisplayName));
    }

    [Fact]
    public void A_corrupt_file_starts_an_empty_map_rather_than_failing()
    {
        File.WriteAllText(_dir.File(ClientRegistry.FileName), "{ \"clients\": [ { broken");

        var registry = Registry();

        Assert.Empty(registry.Snapshot());
        Assert.True(registry.Report(Report("[]")).Accepted);
    }
}
