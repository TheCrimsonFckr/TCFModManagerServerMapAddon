using System.Reflection;
using System.Runtime.Loader;

namespace TCFModManager.ServerMap.Contract;

// What the loader found, or everything it looked at and why it gave up.
public sealed record PayloadLoadResult
{
    public IServerMapPayload? Payload { get; init; }

    public string? PayloadPath { get; init; }

    // Every directory checked, in order, so a failed load says where to put the folder rather than
    // just that it is missing.
    public List<string> Searched { get; init; } = [];

    public Exception? Error { get; init; }

    public bool Loaded => Payload is not null;
}

//
// Finds TCFModManager\ServerMap\payload\ by walking up from the stub's own folder, and loads the
// payload assembly out of it.
//
// Walking up rather than resolving the SPT layout: from
// <serverRoot>\user\mods\TCFMM.ServerMap\ the install root is four levels up on 4.1 and three on
// 3.x, and the stub has no business knowing which. The folder it is looking for is the anchor.
//
public static class PayloadLoader
{
    public const string PayloadAssemblyName = "TCFMM.ServerMap.Payload.dll";

    private static readonly string[] PayloadFolder = ["TCFModManager", "ServerMap", "payload"];

    public static PayloadLoadResult Load(string stubDirectory)
    {
        var searched = new List<string>();

        var directory = new DirectoryInfo(stubDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine([directory.FullName, .. PayloadFolder]);
            searched.Add(candidate);

            var assemblyPath = Path.Combine(candidate, PayloadAssemblyName);
            if (File.Exists(assemblyPath)) return LoadFrom(assemblyPath, searched);

            directory = directory.Parent;
        }

        return new PayloadLoadResult { Searched = searched };
    }

    private static PayloadLoadResult LoadFrom(string assemblyPath, List<string> searched)
    {
        try
        {
            //
            // Into the context this contract was loaded into, never a private one and never simply
            // Default. The payload references this assembly for IServerMapPayload and has to bind
            // to the instance SPT already loaded: an isolated context would load a second copy and
            // the cast below would fail with "cannot cast X to X".
            //
            // Not Assembly.LoadFrom either - that always loads into Default. SPT loads mods into
            // whatever context its own ModLoader runs in, and when any installed mod has a
            // prepatcher the whole server is re-hosted in a separate "SPT.PrepatchHost" context.
            // Shared.dll then lives only there, and a payload in Default cannot find it at all
            // (FileNotFoundException for TCFMM.ServerMap.Shared). Without a prepatcher both are
            // Default and this is the same as before.
            //
            var context = AssemblyLoadContext.GetLoadContext(typeof(IServerMapPayload).Assembly)
                ?? AssemblyLoadContext.Default;

            // LoadFromAssemblyPath throws when that context already holds an assembly of the same
            // name, where LoadFrom quietly returned it - so look first.
            var name = AssemblyName.GetAssemblyName(assemblyPath).Name;

            var assembly = context.Assemblies.FirstOrDefault(a => a.GetName().Name == name)
                ?? context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            var type = assembly.GetTypes().FirstOrDefault(t =>
                typeof(IServerMapPayload).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

            if (type is null)
            {
                return new PayloadLoadResult
                {
                    PayloadPath = assemblyPath,
                    Searched = searched,
                    Error = new TypeLoadException(
                        $"{Path.GetFileName(assemblyPath)} holds no public type implementing IServerMapPayload."),
                };
            }

            return new PayloadLoadResult
            {
                Payload = (IServerMapPayload?)Activator.CreateInstance(type),
                PayloadPath = assemblyPath,
                Searched = searched,
            };
        }
        catch (Exception ex)
        {
            return new PayloadLoadResult { PayloadPath = assemblyPath, Searched = searched, Error = ex };
        }
    }
}
