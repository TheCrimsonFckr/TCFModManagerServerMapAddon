using System.Net;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap.Stub;

//
// The headers a request carries into the payload. Compiled into BOTH stubs (and the tests) as a
// linked file rather than living in the contract assembly: it is the stubs' own behaviour, and it
// touches no SPT or ASP.NET type, so the one copy can be tested without an SPT install.
//
// The caller's address goes over in a header only the stub sets. Any copy that arrived with the
// request is dropped first, so nobody can claim to be on the server's network; the address itself
// is the socket's, never a forwarded-for header.
//
internal static class StubHeaders
{
    public static Dictionary<string, string> ForPayload(
        IEnumerable<KeyValuePair<string, string>> incoming, IPAddress? remoteAddress)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in incoming)
        {
            if (string.Equals(name, PayloadHeaders.RemoteAddress, StringComparison.OrdinalIgnoreCase)) continue;
            headers[name] = value;
        }

        if (remoteAddress is not null) headers[PayloadHeaders.RemoteAddress] = remoteAddress.ToString();

        return headers;
    }
}
