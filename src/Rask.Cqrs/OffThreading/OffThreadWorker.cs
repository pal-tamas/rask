using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

// The worker's side of [OffThread]: a frame in, the handler run against the worker's own container, a frame out.
//
// Nothing here knows it is in a Web Worker. Rask.Wasm builds the container and binds Handle and Cancel to the worker's
// messages; a test calls them directly, which is the whole path but the postMessage.
internal sealed class OffThreadWorker(IServiceProvider services)
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    // Runs the app's module initializers before its container is built: they are what put the handlers into
    // CqrsRegistry, and AddRaskCqrs registers what is there when it is called. The page never calls the worker's Main,
    // so nothing else would run them. A handler in a library of its own is found the same way the page finds it — by
    // the app having touched that library by then (docs/cqrs.md, Limitations); walking every reference instead would
    // need metadata the trimmer does not promise to keep.
    internal static void Load(string mainAssembly) => Initialized(new AssemblyName(mainAssembly));

    internal async Task<byte[]> Handle(int id, byte[] frame)
    {
        using var cancellation = new CancellationTokenSource();
        _running[id] = cancellation;
        try
        {
            return await Answer(frame, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }

    // A handler that looks at Current.Cancellation stops; one that never yields cannot hear this at all, and the page
    // restarts the worker instead.
    internal void Cancel(int id)
    {
        if (_running.TryGetValue(id, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private async Task<byte[]> Answer(byte[] frame, CancellationToken cancellationToken)
    {
        try
        {
            var request = OffThreadRequest.Received.Parse(frame);
            var contract = Contract(request);
            var invoke = contract.LocalInvoker
                         ?? throw new InvalidOperationException(
                             $"'{request.Name}' has no handler in this app, so its worker has nothing to run.");

            ReadAs(request);
            var message = request.Message(contract);

            var scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                using var work = Ambient.Enter(scope.ServiceProvider);
                var result = await invoke(scope.ServiceProvider, message, cancellationToken).ConfigureAwait(false);
                return await OffThreadAnswer.Result(contract, result, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Every exception becomes the page's: a worker that let one escape would answer nothing.
        catch (Exception thrown)
#pragma warning restore CA1031
        {
            return OffThreadAnswer.Fault(thrown);
        }
    }

    // The page names the assembly the message was declared in, so a message whose codec registers from an assembly the
    // worker has not touched yet is still found.
    private static RemoteContract Contract(OffThreadRequest.Received request)
    {
        if (RemoteContractRegistry.TryGet(request.Name, out var contract))
        {
            return contract!;
        }

        TryInitialize(new AssemblyName(request.Assembly));
        return RemoteContractRegistry.TryGet(request.Name, out contract)
            ? contract!
            : throw new InvalidOperationException($"The worker has no wire contract named '{request.Name}'.");
    }

    // The page's language, for this request: a handler that formats a number or reads a resource does it as the page
    // would have. CurrentCulture flows with the async call, so it is this request's alone.
    private static void ReadAs(OffThreadRequest.Received request)
    {
        if (Culture(request.Culture) is { } culture)
        {
            CultureInfo.CurrentCulture = culture;
        }

        if (Culture(request.UiCulture) is { } ui)
        {
            CultureInfo.CurrentUICulture = ui;
        }
    }

    // An app published with invariant globalization knows only the invariant culture, and asking it for another throws.
    private static CultureInfo? Culture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static Assembly Initialized(AssemblyName name)
    {
        var assembly = Assembly.Load(name);
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        return assembly;
    }

    // A reference the trimmer removed, or one the app never ships, is nothing to initialize.
    private static void TryInitialize(AssemblyName name)
    {
        try
        {
            Initialized(name);
        }
        catch (Exception missing) when (missing is FileNotFoundException or FileLoadException)
        {
            // Nothing to initialize: the handlers it would have registered are not in this app either.
            _ = missing;
        }
    }
}
