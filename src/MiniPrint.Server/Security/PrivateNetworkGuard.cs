using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace MiniPrint.Server.Security;

public sealed class PrivateNetworkGuard
{
    private readonly RequestDelegate _next;
    private readonly MiniPrintOptions _options;
    private readonly ILogger<PrivateNetworkGuard> _logger;

    public PrivateNetworkGuard(
        RequestDelegate next,
        IOptions<MiniPrintOptions> options,
        ILogger<PrivateNetworkGuard> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (!_options.AllowPrivateNetworksOnly || IsPrivateOrLoopback(address))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning("Rejected MiniPrint request from non-private address {RemoteAddress}", address);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
    }

    public static bool IsPrivateOrLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
               ((bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80));
    }
}
