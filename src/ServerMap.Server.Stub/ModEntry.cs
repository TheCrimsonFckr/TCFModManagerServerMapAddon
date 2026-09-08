using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.1's IOnLoad is OnLoadAsync(CancellationToken), not OnLoad().
//
// Everything this does is find the payload and hand it to the listener. If it cannot, the mod stays
// loaded and inert: SPT is not told the mod failed, because the mod has not failed - its payload
// folder has been deleted, which is the supported way to remove this feature.
//
[Injectable]
public class ModEntry(ISptLogger<ModEntry> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var stubDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        if (string.IsNullOrEmpty(stubDirectory))
        {
            logger.Warning("[TCFMM ServerMap] Could not resolve this mod's own folder; not loading a payload.");
            return Task.CompletedTask;
        }

        var result = PayloadLoader.Load(stubDirectory);

        if (result.Loaded)
        {
            ServerMapListener.Payload = result.Payload;
            ServerMapListener.RoutePrefix = result.Payload!.RoutePrefix;

            logger.Success(
                $"[TCFMM ServerMap] Ready - serving {ServerMapListener.RoutePrefix} from {result.PayloadPath}");

            return Task.CompletedTask;
        }

        if (result.Error is not null)
        {
            logger.Error($"[TCFMM ServerMap] Found {result.PayloadPath} but could not load it: {result.Error}");
            return Task.CompletedTask;
        }

        // The ordinary "feature removed" path, and the one line the design promises in that case.
        logger.Info(
            $"[TCFMM ServerMap] No payload found, so this mod does nothing. Looked for "
            + $"{PayloadLoader.PayloadAssemblyName} in: {string.Join(", ", result.Searched)}");

        return Task.CompletedTask;
    }
}
