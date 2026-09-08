namespace TCFModManager.ServerMap.Contract;

//
// One request, flattened. The stub is the only thing that touches ASP.NET types; the payload gets
// plain data, so widening the feature never means rebuilding the stub sitting in user\mods.
//
// Body is the raw bytes as they arrived. Nothing between the wire and here decompresses or rewrites
// them - see the /echo route, which exists to prove exactly that.
//
public sealed record PayloadRequest(
    string Method,
    string Path,
    string? Query,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

// One response, fully formed by the payload. The stub writes it and understands none of it.
public sealed record PayloadResponse(int StatusCode, string ContentType, string Body);

//
// The entire contract between the stub in user\mods and the payload in TCFModManager\ServerMap\.
// Deliberately this small: every route, every piece of state and every decision lives on the far
// side of it.
//
// It lives in its own net9.0, SPT-free, ASP.NET-free assembly so that ONE payload build serves both
// SPT 4.0.13 (.NET 9) and SPT 4.1.x (.NET 10). The two SPT versions disagree about five things -
// the runtime, IOnLoad, the mod metadata type, and both IHttpListener methods - and every one of
// those disagreements is confined to the stub. Nothing on this side of the contract knows which
// server it is running under, which is the whole reason the split exists.
//
public interface IServerMapPayload
{
    // The route prefix this payload owns, e.g. "/tcfservermap". Read once, at load.
    string RoutePrefix { get; }

    Task<PayloadResponse> HandleAsync(PayloadRequest request, CancellationToken cancellationToken);
}
