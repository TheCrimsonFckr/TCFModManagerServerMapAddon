using System.Text.Json;

namespace TCFModManager.ServerMap.Tests;

public class ProfileOwnerTests : IDisposable
{
    private const string Mine = "6ac8acae28793133fcefe63e";
    private const string Theirs = "6abee519842869498c34024b";
    private const string Me = "11111111-1111-1111-1111-111111111111";
    private const string Them = "22222222-2222-2222-2222-222222222222";

    private const string Profile = "{ \"info\": { \"id\": \"x\", \"username\": \"u\" }, \"characters\": { \"pmc\": {}, \"scav\": {} } }";

    private readonly TempDirectory _profiles = new();

    private readonly PayloadDriver _server;

    public ProfileOwnerTests() => _server = new PayloadDriver(profilesDirectory: _profiles.Path);

    public void Dispose()
    {
        _server.Dispose();
        _profiles.Dispose();
    }

    private void Make(string id, TimeSpan age)
    {
        var file = _profiles.File(id + ".json");
        File.WriteAllText(file, Profile);
        File.SetCreationTimeUtc(file, _server.Clock.Now.UtcDateTime - age);
    }

    private void Rules(bool hide, bool protect) =>
        File.WriteAllText(_server.File(ServerMapSettings.FileName),
            $"{{ \"hideOtherProfiles\": {(hide ? "true" : "false")}, \"protectOtherProfiles\": {(protect ? "true" : "false")} }}");

    private Reply Claim(string clientId, string profileId) =>
        _server.Post("/claim", JsonSerializer.Serialize(new { clientId, profileId }));

    private Reply Profiles(string? clientId, string address = PayloadDriver.Local) =>
        clientId is null
            ? _server.Get("/profiles", address: address)
            : _server.Get("/profiles", true, address, ("X-ServerMap-Client", clientId));

    private static IEnumerable<string?> Owned(Reply reply) =>
        reply.Json.GetProperty("owned").EnumerateArray().Select(e => e.GetString());

    [Fact]
    public void With_no_rules_set_nothing_is_hidden_or_protected_and_nothing_is_owned()
    {
        var reply = Profiles(Me);

        Assert.Equal(200, reply.Status);
        Assert.False(reply.Json.GetProperty("hideOtherProfiles").GetBoolean());
        Assert.False(reply.Json.GetProperty("protectOtherProfiles").GetBoolean());
        Assert.Empty(Owned(reply));
    }

    [Fact]
    public void A_machine_claims_the_profile_it_just_made()
    {
        Make(Mine, TimeSpan.FromMinutes(1));

        Assert.True(Claim(Me, Mine).Json.GetProperty("claimed").GetBoolean());
        Assert.Equal([Mine], Owned(Profiles(Me)));
        Assert.Empty(Owned(Profiles(Them)));
        Assert.True(File.Exists(_server.File(ProfileOwners.FileName)));
    }

    [Fact]
    public void A_profile_older_than_the_claim_window_belongs_to_nobody()
    {
        Make(Theirs, TimeSpan.FromHours(3));

        var reply = Claim(Me, Theirs);

        Assert.False(reply.Json.GetProperty("claimed").GetBoolean());
        Assert.Equal("tooOld", reply.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public void A_profile_another_machine_claimed_stays_theirs()
    {
        Make(Mine, TimeSpan.FromMinutes(1));
        Claim(Them, Mine);

        var reply = Claim(Me, Mine);

        Assert.False(reply.Json.GetProperty("claimed").GetBoolean());
        Assert.Equal("ownedByAnother", reply.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public void A_profile_that_does_not_exist_cannot_be_claimed()
    {
        Assert.Equal("noSuchProfile", Claim(Me, Mine).Json.GetProperty("reason").GetString());
    }

    [Fact]
    public void The_host_assigning_a_profile_by_hand_is_read_without_a_restart()
    {
        Profiles(Me);
        File.WriteAllText(_server.File(ProfileOwners.FileName), $"{{ \"owners\": {{ \"{Theirs}\": \"{Me}\" }} }}");

        Assert.Equal([Theirs], Owned(Profiles(Me)));
    }

    [Fact]
    public void The_rules_are_reported_to_other_machines()
    {
        Rules(hide: true, protect: true);

        var reply = Profiles(Me);

        Assert.True(reply.Json.GetProperty("hideOtherProfiles").GetBoolean());
        Assert.True(reply.Json.GetProperty("protectOtherProfiles").GetBoolean());
    }

    [Fact]
    public void The_server_machine_itself_is_under_no_rules()
    {
        Rules(hide: true, protect: true);

        var reply = Profiles(Me, "127.0.0.1");

        Assert.False(reply.Json.GetProperty("hideOtherProfiles").GetBoolean());
        Assert.False(reply.Json.GetProperty("protectOtherProfiles").GetBoolean());
    }

    [Fact]
    public void With_profiles_hidden_another_machines_profile_is_not_sent()
    {
        Make(Mine, TimeSpan.FromMinutes(1));
        Make(Theirs, TimeSpan.FromMinutes(1));
        Claim(Me, Mine);
        Claim(Them, Theirs);
        Rules(hide: true, protect: false);

        var theirs = _server.Get($"/profile?id={Theirs}", true, PayloadDriver.Local, ("X-ServerMap-Client", Me));
        var mine = _server.Get($"/profile?id={Mine}", true, PayloadDriver.Local, ("X-ServerMap-Client", Me));
        var host = _server.Get($"/profile?id={Theirs}", true, "127.0.0.1");

        Assert.True(theirs.Json.GetProperty("hidden").GetBoolean());
        Assert.Equal(JsonValueKind.Null, theirs.Json.GetProperty("profile").ValueKind);
        Assert.Equal(JsonValueKind.Object, mine.Json.GetProperty("profile").ValueKind);
        Assert.Equal(JsonValueKind.Object, host.Json.GetProperty("profile").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"clientId\": \"not-a-guid\", \"profileId\": \"6ac8acae28793133fcefe63e\" }")]
    [InlineData("{ \"clientId\": \"11111111-1111-1111-1111-111111111111\", \"profileId\": \"../x\" }")]
    public void A_claim_needs_a_client_id_and_a_profile_id(string body)
    {
        Assert.Equal(400, _server.Post("/claim", body).Status);
    }
}
