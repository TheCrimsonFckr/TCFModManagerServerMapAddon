using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers.Http;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.0.13's IHttpListener: CanHandle takes the session id as well as the context, and the handler
// is Handle rather than HandleAsync and takes no cancellation token. 4.1 dropped the session from
// CanHandle (it is resolved after listener selection there) and renamed the handler.
//
// The session id is ignored in both. This mod serves a published list and a handshake; neither is
// per-player, and a route that answered differently depending on who asked would be a surprise the
// client has no way to see.
//
[Injectable(InjectionType.Singleton)]
public class ServerMapListener(ISptLogger<ServerMapListener> logger) : IHttpListener
{
    // Set by ModEntry once the payload is found. Null means the payload folder is gone.
    public static IServerMapPayload? Payload { get; set; }

    // Kept separately from Payload so an unloaded stub still recognises its own routes and can say
    // why it isn't serving them, rather than falling through to a 404 from somewhere else.
    public static string RoutePrefix { get; set; } = "/tcfservermap";

    public bool CanHandle(MongoId sessionId, HttpContext context) =>
        context.Request.Path.StartsWithSegments(RoutePrefix, StringComparison.OrdinalIgnoreCase);

    public async Task Handle(MongoId sessionId, HttpContext context)
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
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);

            var headers = context.Request.Headers.ToDictionary(
                h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var request = new PayloadRequest(
                context.Request.Method,
                path,
                context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                headers,
                buffer.ToArray());

            //
            // 4.0 hands the listener no cancellation token, so the one the request itself carries is
            // used instead. Same meaning, and the payload's contract is unchanged either way.
            //
            var response = await Payload.HandleAsync(request, context.RequestAborted).ConfigureAwait(false);

            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"[TCFMM ServerMap] Error handling {path}: {ex}");
            context.Response.StatusCode = 500;
        }
    }
}
