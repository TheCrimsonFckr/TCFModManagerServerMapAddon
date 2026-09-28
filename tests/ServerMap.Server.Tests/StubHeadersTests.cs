using System.Net;
using TCFModManager.ServerMap.Contract;
using TCFModManager.ServerMap.Stub;

namespace TCFModManager.ServerMap.Tests;

// The one copy of the header handling both stubs compile (src\ServerMap.Server.Stub.Common).
public class StubHeadersTests
{
    private static KeyValuePair<string, string> H(string name, string value) => new(name, value);

    [Fact]
    public void The_socket_address_is_passed_on()
    {
        var headers = StubHeaders.ForPayload([H("Accept", "*/*")], IPAddress.Parse("192.168.1.20"));

        Assert.Equal("192.168.1.20", headers[PayloadHeaders.RemoteAddress]);
        Assert.Equal("*/*", headers["Accept"]);
    }

    [Theory]
    [InlineData("X-TCFMM-Remote-Address")]
    [InlineData("x-tcfmm-remote-address")]
    public void A_caller_cannot_claim_an_address(string spoofed)
    {
        var headers = StubHeaders.ForPayload([H(spoofed, "127.0.0.1")], IPAddress.Parse("203.0.113.9"));

        Assert.Equal("203.0.113.9", headers[PayloadHeaders.RemoteAddress]);
        Assert.Single(headers);
    }

    [Fact]
    public void No_socket_address_means_no_header_even_a_spoofed_one()
    {
        var headers = StubHeaders.ForPayload([H(PayloadHeaders.RemoteAddress, "127.0.0.1")], null);

        Assert.False(headers.ContainsKey(PayloadHeaders.RemoteAddress));
    }

    [Fact]
    public void A_mapped_IPv6_address_arrives_as_the_socket_wrote_it_and_still_counts_as_local()
    {
        var headers = StubHeaders.ForPayload([], IPAddress.Parse("192.168.1.20").MapToIPv6());

        Assert.Equal("::ffff:192.168.1.20", headers[PayloadHeaders.RemoteAddress]);
        Assert.True(LocalNetwork.IsLocal(headers[PayloadHeaders.RemoteAddress]));
    }

    [Fact]
    public void Header_names_are_looked_up_without_case_as_the_payload_does()
    {
        var headers = StubHeaders.ForPayload([H("X-ServerMap-Key", "ABCD")], null);

        Assert.Equal("ABCD", headers["x-servermap-key"]);
    }
}
