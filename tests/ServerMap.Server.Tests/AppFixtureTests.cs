using System.Text.Json;

namespace TCFModManager.ServerMap.Tests;

//
// The formats this mod shares with TCFModManager, proven against files the app itself wrote rather
// than JSON typed out here - the PascalCase-against-camelCase bug was exactly a hand-typed fixture
// agreeing with a hand-typed reader.
//
//   Fixtures\app-published.tcfmodlist  ModListFile.Write: a schema 4 list, three entries, mixed scopes
//   Fixtures\app-report.json           ServerMapReporting.Build, serialised as ServerMapClient sends it
//
// Regenerate both from TCFModManager.Core when either format changes, and keep the old ones as
// extra cases if an older app can still send them.
//
public class AppFixtureTests : IDisposable
{
    private readonly PayloadDriver _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public void A_list_the_app_exported_is_published_and_described()
    {
        var read = PublishedModList.ReadFrom(Fixture.PathOf("app-published.tcfmodlist"))!;

        Assert.Equal("Zero to Hero", read.Name);
        Assert.Equal(7, read.Revision);
        Assert.Equal(3, read.EntryCount);
        Assert.Equal("4.1.5", read.SptVersion);
    }

    [Fact]
    public void A_list_the_app_exported_is_served_untouched()
    {
        var file = Fixture.Text("app-published.tcfmodlist");
        _server.Publish(file);

        Assert.Equal(file, _server.Get("/list").Body);
    }

    [Fact]
    public void A_report_the_app_sent_is_accepted_and_its_mods_come_back_as_sent()
    {
        var report = Fixture.Text("app-report.json");

        var reply = _server.Post("/report", report);
        Assert.Equal(200, reply.Status);
        Assert.False(reply.Json.GetProperty("resend").GetBoolean());

        var row = _server.Get("/clients", headers: (ServerMapPayload.ClientHeaderName, "3b0c5a52-8f3e-4d0e-a3a5-6a1f7f6b2c90"))
            .Json.GetProperty("clients")[0];

        var sent = JsonDocument.Parse(report).RootElement;
        Assert.Equal(Fixture.Compact(sent.GetProperty("mods")), Fixture.Compact(row.GetProperty("mods")));
        Assert.Equal("Chris PC", row.GetProperty("displayName").GetString());
        Assert.True(row.GetProperty("isYou").GetBoolean());
        Assert.True(row.GetProperty("gameRunning").GetBoolean());
        Assert.Equal("4.1.5", row.GetProperty("sptVersion").GetString());
    }

    // The fields ServerMapClient.ClientsAsync reads, by the names it reads them.
    [Fact]
    public void The_map_carries_every_field_the_app_reads()
    {
        _server.Post("/report", Fixture.Text("app-report.json"));

        var body = _server.Get("/clients").Json;
        Assert.Equal(1, body.GetProperty("protocol").GetInt32());
        Assert.Equal(60, body.GetProperty("intervalSeconds").GetInt32());

        var row = body.GetProperty("clients")[0];
        foreach (var name in new[]
                 {
                     "isYou", "displayName", "hosts", "plays", "headless", "gameRunning", "sptVersion", "appVersion",
                     "firstSeen", "lastSeen", "secondsSinceSeen", "inventoryReportedAt", "mods",
                 })
        {
            Assert.True(row.TryGetProperty(name, out _), $"/clients rows lost '{name}'");
        }
    }
}
