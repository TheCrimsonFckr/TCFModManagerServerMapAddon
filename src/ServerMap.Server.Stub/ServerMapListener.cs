using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.1's IHttpListener: CanHandle takes only the HttpContext (the session is resolved after
// listener selection, and is no longer part of it), and Handle became HandleAsync with a
// CancellationToken. A listener written against 4.0.13 does not compile here.
//
[Injectable(InjectionType.Singleton)]
public class ServerMapListener(ISptLogger<ServerMapListener> logger) : IHttpListener
{
    // Set by ModEntry once the payload is found. Null means the payload folder is gone.
    public static IServerMapPayload? Payload { get; set; }

    // Kept separately from Payload so an unloaded stub still recognises its own routes and can say
    // why it isn't serving them, rather than falling through to a 404 from somewhere else.
    public static string RoutePrefix { get; set; } = "/tcfservermap";

    public bool CanHandle(HttpContext context) =>
        context.Request.Path.StartsWithSegments(RoutePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task HandleAsync(MongoId sessionId, HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (Payload is null)
        {
            logger.Warning(
                $"[TCFMM ServerMap] {path} was requested but the payload is not loaded - see the startup log.");
            context.Response.StatusCode = 503;
            return;
        }

        try
        {
            //
            // Read the body as it arrived, without interpreting it. SPT's own SptHttpListener
            // zlib-decompresses request bodies, but that is *its* listener - ours handles the
            // request outright, so nothing in that path runs. The /echo route proves it.
            //
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            var headers = context.Request.Headers.ToDictionary(
                h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var request = new PayloadRequest(
                context.Request.Method,
                path,
                context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                headers,
                buffer.ToArray());

            var response = await Payload.HandleAsync(request, cancellationToken).ConfigureAwait(false);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"[TCFMM ServerMap] Error handling {path}: {ex}");
            context.Response.StatusCode = 500;
        }
    }
}
