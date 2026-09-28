using System.Text.Json;

namespace TCFModManager.ServerMap.Tests;

public class PayloadRouteTests : IDisposable
{
    private readonly PayloadDriver _server = new();

    public void Dispose() => _server.Dispose();

    private const string List = "{ \"list\": { \"name\": \"Zero to Hero\", \"revision\": 7, \"entries\": [ {}, {}, {} ] } }";

    private static string ReportJson(string id, string? mods = "[]", string hash = "h1", int protocol = 1, string name = "Box") =>
        JsonSerializer.Serialize(new
        {
            protocol,
            clientId = id,
            displayName = name,
            plays = true,
            inventoryHash = hash,
            mods = mods is null ? (JsonElement?)null : JsonDocument.Parse(mods).RootElement,
        });

    [Fact]
    public void Starting_the_payload_writes_the_key_before_anyone_asks()
    {
        Assert.True(File.Exists(_server.File(ServerMapKey.FileName)));
    }

    [Fact]
    public void Hello_needs_no_key_and_says_what_the_server_offers()
    {
        var reply = _server.Get("/hello", withKey: false);

        Assert.Equal(200, reply.Status);
        Assert.Equal(1, reply.Json.GetProperty("protocol").GetInt32());
        Assert.True(reply.Json.GetProperty("requiresKey").GetBoolean());
        Assert.False(reply.Json.GetProperty("hasList").GetBoolean());
        Assert.Equal(60, reply.Json.GetProperty("reportIntervalSeconds").GetInt32());
        Assert.Equal(["hello", "map", "echo"], reply.Json.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Hello_carries_the_published_list_revision()
    {
        _server.Publish(List);

        var hello = _server.Get("/hello", withKey: false).Json;

        Assert.True(hello.GetProperty("hasList").GetBoolean());
        Assert.Equal(7, hello.GetProperty("listRevision").GetInt32());
        Assert.Equal("Zero to Hero", hello.GetProperty("listName").GetString());
        Assert.Equal(3, hello.GetProperty("listEntryCount").GetInt32());
        Assert.Contains("list", hello.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("GET", "/list")]
    [InlineData("GET", "/clients")]
    [InlineData("POST", "/report")]
    [InlineData("POST", "/withdraw")]
    [InlineData("POST", "/echo")]
    [InlineData("GET", "/nothing-here")]
    public void Every_route_but_hello_needs_the_key(string method, string route)
    {
        var reply = _server.Send(method, route, "{}", key: null);

        Assert.Equal(401, reply.Status);
        Assert.DoesNotContain(_server.Key, reply.Body);
    }

    [Fact]
    public void A_wrong_key_is_401_and_says_no_more()
    {
        var reply = _server.Send("GET", "/list", key: "AAAA-BBBB-CCCC-DDDD-EEEE-FFFF");

        Assert.Equal(401, reply.Status);
        Assert.Equal(["protocol", "error"], reply.Json.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_key_is_accepted_however_it_was_typed()
    {
        _server.Publish(List);

        var typed = " " + _server.Key.ToLowerInvariant().Replace("-", " ") + " ";

        Assert.Equal(200, _server.Send("GET", "/list", key: typed).Status);
    }

    [Fact]
    public void List_is_404_without_giving_away_the_config_folder()
    {
        var reply = _server.Get("/list");

        Assert.Equal(404, reply.Status);
        Assert.DoesNotContain(_server.ConfigDirectory, reply.Body);
        Assert.DoesNotContain("Data", reply.Body);
    }

    [Fact]
    public void List_is_the_file_byte_for_byte()
    {
        _server.Publish(List);

        var reply = _server.Get("/list");

        Assert.Equal(200, reply.Status);
        Assert.Equal(List, reply.Body);
        Assert.Equal("application/json", reply.ContentType);
    }

    [Fact]
    public void Routes_ignore_case()
    {
        Assert.Equal(200, _server.Get("/HELLO", withKey: false).Status);
    }

    [Fact]
    public void An_unknown_route_lists_the_real_ones()
    {
        var reply = _server.Get("/files");

        Assert.Equal(404, reply.Status);
        Assert.Contains("/clients", reply.Json.GetProperty("routes").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [InlineData("/report")]
    [InlineData("/withdraw")]
    public void Report_and_withdraw_take_a_post(string route)
    {
        Assert.Equal(405, _server.Get(route).Status);
    }

    [Fact]
    public void A_first_report_without_mods_is_asked_to_resend()
    {
        var reply = _server.Post("/report", ReportJson(Guid.NewGuid().ToString(), mods: null));

        Assert.Equal(200, reply.Status);
        Assert.True(reply.Json.GetProperty("resend").GetBoolean());
        Assert.Equal(60, reply.Json.GetProperty("intervalSeconds").GetInt32());
    }

    [Fact]
    public void A_report_is_refused_when_it_cannot_be_read()
    {
        Assert.Equal(400, _server.Post("/report", "{ nope").Status);
        Assert.Equal(400, _server.Post("/report", "null").Status);
        Assert.Equal(400, _server.Post("/report", ReportJson("not-a-guid")).Status);
        Assert.Equal(409, _server.Post("/report", ReportJson(Guid.NewGuid().ToString(), protocol: 2)).Status);
    }

    [Fact]
    public void A_report_over_a_megabyte_is_refused_before_it_is_read()
    {
        var big = ReportJson(Guid.NewGuid().ToString(), name: new string('x', 1024 * 1024));

        Assert.Equal(413, _server.Post("/report", big).Status);
    }

    [Fact]
    public void The_map_marks_the_caller_and_never_hands_out_an_id()
    {
        var mine = Guid.NewGuid().ToString();
        var theirs = Guid.NewGuid().ToString();
        _server.Post("/report", ReportJson(mine, name: "Mine"));
        _server.Post("/report", ReportJson(theirs, name: "Theirs"));

        var reply = _server.Get("/clients", headers: (ServerMapPayload.ClientHeaderName, mine.ToUpperInvariant()));

        Assert.Equal(200, reply.Status);
        Assert.DoesNotContain(mine, reply.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(theirs, reply.Body, StringComparison.OrdinalIgnoreCase);

        var rows = reply.Json.GetProperty("clients").EnumerateArray().ToList();
        Assert.True(rows.Single(r => r.GetProperty("displayName").GetString() == "Mine").GetProperty("isYou").GetBoolean());
        Assert.False(rows.Single(r => r.GetProperty("displayName").GetString() == "Theirs").GetProperty("isYou").GetBoolean());
    }

    [Fact]
    public void Seconds_since_seen_follow_the_servers_clock()
    {
        _server.Post("/report", ReportJson(Guid.NewGuid().ToString()));
        _server.Clock.Advance(TimeSpan.FromMinutes(3));

        var row = _server.Get("/clients").Json.GetProperty("clients")[0];

        Assert.Equal(180, row.GetProperty("secondsSinceSeen").GetInt64());
    }

    [Fact]
    public void Withdraw_takes_the_machine_off_the_map()
    {
        var id = Guid.NewGuid().ToString();
        _server.Post("/report", ReportJson(id));

        var reply = _server.Post("/withdraw", JsonSerializer.Serialize(new { clientId = id }));

        Assert.True(reply.Json.GetProperty("removed").GetBoolean());
        Assert.Empty(_server.Get("/clients").Json.GetProperty("clients").EnumerateArray());
        Assert.False(_server.Post("/withdraw", JsonSerializer.Serialize(new { clientId = id })).Json.GetProperty("removed").GetBoolean());
        Assert.Equal(400, _server.Post("/withdraw", "[").Status);
    }

    [Fact]
    public void Echo_reports_the_body_exactly_as_it_arrived()
    {
        var reply = _server.Post("/echo", "héllo {\"a\":1}");

        Assert.Equal("héllo {\"a\":1}", reply.Json.GetProperty("bodyAsText").GetString());
        Assert.Equal(14, reply.Json.GetProperty("receivedBytes").GetInt32());
        Assert.False(reply.Json.GetProperty("looksZlibCompressed").GetBoolean());
    }
}
