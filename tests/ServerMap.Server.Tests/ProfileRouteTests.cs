using System.Text.Json;

namespace TCFModManager.ServerMap.Tests;

public class ProfileRouteTests : IDisposable
{
    private const string Id = "6ac8acae28793133fcefe63e";

    private const string Profile =
        "{ \"info\": { \"id\": \"6ac8acae28793133fcefe63e\", \"username\": \"Zero\", \"edition\": \"Standard\" }, "
        + "\"characters\": { \"pmc\": { \"Info\": { \"Nickname\": \"TheCrimsonZero\", \"Level\": 5 } }, \"scav\": {} } }";

    private readonly TempDirectory _profiles = new();

    private readonly PayloadDriver _server;

    public ProfileRouteTests() => _server = new PayloadDriver(profilesDirectory: _profiles.Path);

    public void Dispose()
    {
        _server.Dispose();
        _profiles.Dispose();
    }

    private string Save(string json = Profile)
    {
        var file = _profiles.File(Id + ".json");
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void A_saved_profile_comes_back_whole_with_when_it_was_written()
    {
        var file = Save();

        var reply = _server.Get($"/profile?id={Id}");

        Assert.Equal(200, reply.Status);
        Assert.Equal(Id, reply.Json.GetProperty("profileId").GetString());
        Assert.False(reply.Json.GetProperty("unchanged").GetBoolean());
        Assert.False(reply.Json.GetProperty("busy").GetBoolean());
        Assert.Equal(new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds(), reply.Json.GetProperty("written").GetInt64());
        Assert.Equal(Fixture.Compact(Profile), Fixture.Compact(reply.Json.GetProperty("profile")));
    }

    [Fact]
    public void An_unchanged_profile_is_not_sent_again()
    {
        Save();
        var written = _server.Get($"/profile?id={Id}").Json.GetProperty("written").GetInt64();

        var reply = _server.Get($"/profile?id={Id}&since={written}");

        Assert.True(reply.Json.GetProperty("unchanged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("profile").ValueKind);
    }

    [Fact]
    public void A_profile_this_server_does_not_have_is_null()
    {
        var reply = _server.Get($"/profile?id={Id}");

        Assert.Equal(200, reply.Status);
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("profile").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("written").ValueKind);
    }

    [Fact]
    public void A_profile_caught_half_written_is_busy_not_broken()
    {
        Save("{ \"info\": { \"id\": ");

        var reply = _server.Get($"/profile?id={Id}");

        Assert.Equal(200, reply.Status);
        Assert.True(reply.Json.GetProperty("busy").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("profile").ValueKind);
    }

    [Theory]
    [InlineData("/profile")]
    [InlineData("/profile?id=..%2F..%2Fservermap")]
    [InlineData("/profile?id=6ac8acae28793133fcefe63")]
    [InlineData("/profile?id=6ac8acae28793133fcefe63e.json")]
    public void Only_a_profile_id_is_accepted(string route)
    {
        Assert.Equal(400, _server.Get(route).Status);
    }

    [Fact]
    public void It_takes_a_GET()
    {
        Save();

        Assert.Equal(405, _server.Send("POST", $"/profile?id={Id}", "{}", _server.Key).Status);
    }

    [Fact]
    public void A_server_whose_profiles_folder_was_not_found_says_so()
    {
        using var server = new PayloadDriver();

        Assert.Equal(500, server.Get($"/profile?id={Id}").Status);
    }
}
