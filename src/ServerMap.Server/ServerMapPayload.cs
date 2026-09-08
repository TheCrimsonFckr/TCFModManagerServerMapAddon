using System.Reflection;
using System.Text;
using System.Text.Json;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap;

//
// Everything this server mod does. Three routes:
//
//   GET /tcfservermap/hello - the handshake TCFModManager probes to decide whether it can talk to
//                             this server. THE ONLY UNAUTHENTICATED ROUTE, on purpose: it is asked
//                             before the user has been given a key, it discloses nothing a port scan
//                             would not, and gating it would leave a client unable to tell "wrong
//                             address" from "right address, no key".
//   GET /tcfservermap/list  - the mod list this server publishes, served verbatim from the file the
//                             operator dropped into config\. See PublishedModList. Needs the key.
//   POST /tcfservermap/echo - diagnostic. Reports the request body exactly as it arrived. Kept from
//                             the transport spike because "the body arrived mangled" is the one
//                             class of bug that is impossible to reason about without it. Needs the
//                             key - a diagnostic that reflects input is still a route.
//
// This mod serves a LIST, never mod files. That is the whole design: TCFModManager installs from
// The Forge and nowhere else, so a server that could push bytes would break the one rule the app is
// built on. There is no file route, and adding one is not a small change.
//
public sealed class ServerMapPayload : IServerMapPayload
{
    private const int Protocol = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _configDirectory =
        PublishedModList.ConfigDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".");

    public ServerMapPayload()
    {
        //
        // Generated when the server starts, not when someone first fails to authenticate.
        //
        // The operator needs the key BEFORE anyone connects - it is the thing they send out with the
        // address. Generating it lazily meant the file did not exist until a stranger had already
        // been refused, which is exactly backwards: the person who needs it first is the one running
        // the server, and they should find it waiting for them.
        //
        ServerMapKey.Current(_configDirectory);
    }

    public string RoutePrefix => "/tcfservermap";

    public Task<PayloadResponse> HandleAsync(PayloadRequest request, CancellationToken cancellationToken)
    {
        var route = request.Path.Length > RoutePrefix.Length ? request.Path[RoutePrefix.Length..] : "";

        var path = route.ToLowerInvariant();

        //
        // Everything but the handshake is gated, checked here rather than per route so adding a
        // route cannot accidentally add an open one.
        //
        if (path != "/hello" && !Authorized(request)) return Task.FromResult(Unauthorized());

        return Task.FromResult(path switch
        {
            "/hello" => Hello(),
            "/list" => List(),
            "/echo" => Echo(request),
            _ => NotFound(route),
        });
    }

    //
    // The handshake carries the published list's revision, on purpose: the cheap probe the client
    // already makes to see whether this server is reachable also answers "is there anything newer
    // than what I hold", so the list itself is only fetched when that number moves.
    //
    private PayloadResponse Hello()
    {
        var published = PublishedModList.Current(_configDirectory);

        var body = new
        {
            protocol = Protocol,
            modVersion = ModVersion(),
            serverName = Environment.MachineName,
            requiresKey = true,
            hasList = published is not null,
            listRevision = published?.Revision,
            listName = published?.Name,
            listEntryCount = published?.EntryCount,
            capabilities = published is not null
                ? new[] { "hello", "list", "echo" }
                : new[] { "hello", "echo" },
        };

        return new PayloadResponse(200, "application/json", JsonSerializer.Serialize(body, Json));
    }

    //
    // The file, byte for byte. It is already the format TCFModManager reads, so re-encoding it here
    // would be a second implementation of one format, free to drift from the real one.
    //
    // 404 when nothing is published, which the client reads as "this server has no list" rather than
    // as a failure - a server can run this mod and deliberately offer nothing.
    //
    private PayloadResponse List()
    {
        var published = PublishedModList.Current(_configDirectory);

        if (published is null)
        {
            //
            // Deliberately does NOT name the config folder. An earlier version returned the full
            // path so an operator reading their own server's reply would know where to put a list -
            // helpful, and also a directory layout handed to anyone who can reach the port. The
            // operator has the app and the README; a stranger gets nothing.
            //
            var body = new
            {
                protocol = Protocol,
                error = "This server does not publish a mod list.",
            };

            return new PayloadResponse(404, "application/json", JsonSerializer.Serialize(body, Json));
        }

        return new PayloadResponse(200, "application/json", published.Json);
    }

    //
    // What arrived, how long it was, whether the first bytes carry a compression header, and whether
    // it reads back as the text that was sent. SPT's own listener zlib-decompresses request bodies;
    // ours handles the request outright so nothing in that path runs, and this is what proves it.
    //
    private static PayloadResponse Echo(PayloadRequest request)
    {
        var body = request.Body;

        var looksZlib = body.Length >= 2 && body[0] == 0x78 && body[1] is 0x01 or 0x5E or 0x9C or 0xDA;
        var looksGzip = body.Length >= 2 && body[0] == 0x1F && body[1] == 0x8B;

        string? asText = null;
        try
        {
            asText = new UTF8Encoding(false, true).GetString(body);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, which would itself mean something rewrote it.
        }

        var report = new
        {
            protocol = Protocol,
            method = request.Method,
            query = request.Query,
            receivedBytes = body.Length,
            firstBytesHex = Convert.ToHexString(body.AsSpan(0, Math.Min(16, body.Length))),
            looksZlibCompressed = looksZlib,
            looksGzipCompressed = looksGzip,
            isValidUtf8 = asText is not null,
            bodyAsText = asText,
            contentType = Header(request, "Content-Type"),
            contentLength = Header(request, "Content-Length"),
            contentEncoding = Header(request, "Content-Encoding"),
            requestCompressed = Header(request, "requestcompressed"),
            allHeaders = request.Headers,
        };

        return new PayloadResponse(200, "application/json", JsonSerializer.Serialize(report, Json));
    }

    private bool Authorized(PayloadRequest request) =>
        ServerMapKey.Verify(ServerMapKey.Current(_configDirectory), Header(request, ServerMapKey.HeaderName));

    //
    // 401 and nothing else. It does not say whether a key was sent, whether it was close, or what
    // the server expects - the client already knows from the handshake that a key is needed, and
    // anyone who does not is not owed the detail.
    //
    private static PayloadResponse Unauthorized()
    {
        var body = new
        {
            protocol = Protocol,
            error = "This route needs the server's shared key. Ask whoever runs the server for it.",
        };

        return new PayloadResponse(401, "application/json", JsonSerializer.Serialize(body, Json));
    }

    private static string? Header(PayloadRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) ? value : null;

    private static PayloadResponse NotFound(string route)
    {
        var body = new
        {
            protocol = Protocol,
            error = $"No route '{route}'.",
            routes = new[] { "/hello", "/list", "/echo" },
        };

        return new PayloadResponse(404, "application/json", JsonSerializer.Serialize(body, Json));
    }

    private static string ModVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "0.0.0";
}
