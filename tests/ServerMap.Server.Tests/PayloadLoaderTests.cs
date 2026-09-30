using System.Runtime.Loader;
using TCFModManager.ServerMap.Contract;

namespace TCFModManager.ServerMap.Tests;

// How the stub finds and loads the payload - the stub/payload split spike S6 proved on a real server.
public class PayloadLoaderTests
{
    private static string StubFolder(TempDirectory root)
    {
        var stub = Path.Combine(root.Path, "SPT_Runtime", "user", "mods", "TCFMM.ServerMap");
        Directory.CreateDirectory(stub);
        return stub;
    }

    private static string PayloadFolder(TempDirectory root)
    {
        var payload = Path.Combine(root.Path, "TCFModManager", "ServerMap", "payload");
        Directory.CreateDirectory(payload);
        return payload;
    }

    [Fact]
    public void No_payload_folder_is_reported_with_every_place_it_looked()
    {
        using var root = new TempDirectory();
        var stub = StubFolder(root);

        var result = PayloadLoader.Load(stub);

        Assert.False(result.Loaded);
        Assert.Null(result.Error);
        Assert.Equal(Path.Combine(stub, "TCFModManager", "ServerMap", "payload"), result.Searched[0]);
        Assert.Contains(Path.Combine(root.Path, "TCFModManager", "ServerMap", "payload"), result.Searched);
    }

    [Fact]
    public void The_payload_is_found_by_walking_up_from_the_stub_and_loaded()
    {
        using var root = new TempDirectory();
        var stub = StubFolder(root);
        var payload = PayloadFolder(root);
        File.Copy(typeof(ServerMapPayload).Assembly.Location, Path.Combine(payload, PayloadLoader.PayloadAssemblyName));

        var result = PayloadLoader.Load(stub);

        Assert.True(result.Loaded, result.Error?.ToString());
        Assert.Equal(Path.Combine(payload, PayloadLoader.PayloadAssemblyName), result.PayloadPath);
        Assert.Equal("/tcfservermap", result.Payload!.RoutePrefix);
    }

    [Fact]
    public void A_payload_file_with_nothing_to_load_says_so()
    {
        using var root = new TempDirectory();
        var stub = StubFolder(root);
        var payload = PayloadFolder(root);
        File.Copy(typeof(IServerMapPayload).Assembly.Location, Path.Combine(payload, PayloadLoader.PayloadAssemblyName));

        var result = PayloadLoader.Load(stub);

        Assert.False(result.Loaded);
        Assert.IsType<TypeLoadException>(result.Error);
    }

    [Fact]
    public void A_payload_file_that_is_not_an_assembly_is_an_error_not_a_crash()
    {
        using var root = new TempDirectory();
        var stub = StubFolder(root);
        File.WriteAllText(Path.Combine(PayloadFolder(root), PayloadLoader.PayloadAssemblyName), "not a dll");

        var result = PayloadLoader.Load(stub);

        Assert.False(result.Loaded);
        Assert.NotNull(result.Error);
    }

    //
    // When any installed mod has a prepatcher, SPT re-hosts itself in its own AssemblyLoadContext
    // ("SPT.PrepatchHost") and loads every mod - Shared.dll included - into that, not Default. The
    // payload has to land in the same context or it cannot see Shared at all. This stands in for
    // that context: Shared is loaded only here, and the loader is called through this copy of it.
    //
    [Fact]
    public void The_payload_loads_into_the_context_the_contract_is_in_not_Default()
    {
        using var root = new TempDirectory();
        var stub = StubFolder(root);
        var payload = PayloadFolder(root);

        var sharedPath = Path.Combine(stub, Path.GetFileName(typeof(IServerMapPayload).Assembly.Location));
        File.Copy(typeof(IServerMapPayload).Assembly.Location, sharedPath);
        File.Copy(typeof(ServerMapPayload).Assembly.Location, Path.Combine(payload, PayloadLoader.PayloadAssemblyName));

        var modContext = new AssemblyLoadContext("TCFMM.Tests.PrepatchHost", isCollectible: true);
        try
        {
            var shared = modContext.LoadFromAssemblyPath(sharedPath);
            var loader = shared.GetType(typeof(PayloadLoader).FullName!, throwOnError: true)!;
            var result = loader.GetMethod(nameof(PayloadLoader.Load))!.Invoke(null, [stub])!;

            var error = result.GetType().GetProperty(nameof(PayloadLoadResult.Error))!.GetValue(result);
            var loaded = result.GetType().GetProperty(nameof(PayloadLoadResult.Payload))!.GetValue(result);

            Assert.True(loaded is not null, error?.ToString());
            Assert.Same(modContext, AssemblyLoadContext.GetLoadContext(loaded!.GetType().Assembly));
        }
        finally
        {
            modContext.Unload();
        }
    }
}
