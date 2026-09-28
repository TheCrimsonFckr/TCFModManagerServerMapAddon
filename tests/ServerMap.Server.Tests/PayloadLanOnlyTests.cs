namespace TCFModManager.ServerMap.Tests;

public class PayloadLanOnlyTests : IDisposable
{
    private readonly PayloadDriver _server = new();

    public void Dispose() => _server.Dispose();

    private static void AssertLanOnlyRefusal(Reply reply)
    {
        Assert.Equal(403, reply.Status);
        Assert.Equal("lanOnly", reply.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public void Off_by_default_an_outside_caller_is_answered()
    {
        Assert.Equal(200, _server.Get("/hello", withKey: false, address: PayloadDriver.Outside).Status);
    }

    [Theory]
    [InlineData("/hello")]
    [InlineData("/list")]
    [InlineData("/clients")]
    public void On_an_outside_caller_is_refused_everywhere_hello_included(string route)
    {
        _server.LanOnly(true);

        AssertLanOnlyRefusal(_server.Get(route, address: PayloadDriver.Outside));
    }

    // Checked before the key: a stranger learns the server is LAN-only, not whether their key works.
    [Fact]
    public void The_refusal_comes_before_the_key()
    {
        _server.LanOnly(true);

        AssertLanOnlyRefusal(_server.Get("/list", withKey: false, address: PayloadDriver.Outside));
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("100.101.102.103")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.5")]
    public void On_a_caller_on_the_network_is_answered(string address)
    {
        _server.LanOnly(true);

        Assert.Equal(200, _server.Get("/clients", address: address).Status);
    }

    [Fact]
    public void On_a_caller_on_the_network_still_needs_the_key()
    {
        _server.LanOnly(true);

        Assert.Equal(401, _server.Get("/clients", withKey: false).Status);
    }

    // What an older stub looks like: it never sends the address. LAN-only fails closed.
    [Fact]
    public void On_with_no_address_from_the_stub_everyone_is_refused()
    {
        _server.LanOnly(true);

        AssertLanOnlyRefusal(_server.Get("/hello", withKey: false, address: null));
    }

    [Fact]
    public void Switching_it_off_takes_effect_on_the_next_request()
    {
        _server.LanOnly(true);
        AssertLanOnlyRefusal(_server.Get("/hello", address: PayloadDriver.Outside));

        _server.LanOnly(false);
        File.SetLastWriteTimeUtc(_server.File(ServerMapSettings.FileName), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(200, _server.Get("/hello", address: PayloadDriver.Outside).Status);
    }
}
