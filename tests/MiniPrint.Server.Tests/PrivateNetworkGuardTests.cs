using System.Net;
using MiniPrint.Server.Security;

namespace MiniPrint.Server.Tests;

public sealed class PrivateNetworkGuardTests
{
    [Fact]
    public void IsPrivateOrLoopback_RejectsMissingRemoteAddress()
    {
        Assert.False(PrivateNetworkGuard.IsPrivateOrLoopback(null));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.20.30.40", true)]
    [InlineData("172.16.1.1", true)]
    [InlineData("172.31.255.254", true)]
    [InlineData("192.168.50.2", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("::1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void IsPrivateOrLoopback_ClassifiesAddress(string input, bool expected)
    {
        Assert.Equal(expected, PrivateNetworkGuard.IsPrivateOrLoopback(IPAddress.Parse(input)));
    }
}
