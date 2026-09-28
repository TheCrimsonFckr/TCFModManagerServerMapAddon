namespace TCFModManager.ServerMap.Tests;

public class LocalNetworkTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.8.9.10")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.10.10")]
    [InlineData("100.64.0.1")]      // Tailscale's range starts here
    [InlineData("100.127.255.254")] // and ends here
    [InlineData("::1")]
    [InlineData("fd7a:115c:a1e0::1")] // unique local, Tailscale's IPv6
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:192.168.1.20")] // IPv4 mapped, as a dual-stack socket reports it
    [InlineData("::ffff:100.100.1.1")]
    public void Counts_as_local(string address) => Assert.True(LocalNetwork.IsLocal(address));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.9")]
    [InlineData("11.0.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("169.253.1.1")]
    [InlineData("2001:db8::1")]
    [InlineData("2606:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("fec0::1")]
    public void Counts_as_outside(string address) => Assert.False(LocalNetwork.IsLocal(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("192.168.1.20, 8.8.8.8")]
    public void Anything_unreadable_is_outside(string? address) => Assert.False(LocalNetwork.IsLocal(address));
}
