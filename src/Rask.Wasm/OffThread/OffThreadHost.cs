#if RASK_BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Wasm.Diagnostics;

namespace Rask.Wasm;

// The worker's .NET end of [OffThread], which Resources/rask-worker.ts calls into. The worker's runtime never runs the
// app's Main, so nothing of the page is here: Start builds a container with the app's Rask.Cqrs handlers, and Handle runs
// one frame against it.
internal static partial class OffThreadHost
{
    private static OffThreadWorker? _worker;

#if RASK_BROWSER
    [JSExport]
#endif
    public static Task Start(string mainAssembly, string culture, string baseAddress)
    {
        if (Culture(culture) is { } read)
        {
            CultureInfo.DefaultThreadCurrentCulture = read;
            CultureInfo.DefaultThreadCurrentUICulture = read;
        }

        OffThreadWorker.Load(mainAssembly);
        _worker = new OffThreadWorker(Services(baseAddress));
        return Task.CompletedTask;
    }

    // Runs one request; the answer goes back through the worker script's `answer` import, as bytes rather than through
    // a promise.
#if RASK_BROWSER
    [JSExport]
#endif
    public static void Handle(int id, byte[] frame) => _ = Answer(id, frame);

    internal static async Task<byte[]> Run(int id, byte[] frame)
    {
        var worker = _worker ?? throw new InvalidOperationException("The off-thread worker was sent a request before it started.");
        return await worker.Handle(id, frame).ConfigureAwait(false);
    }

    private static async Task Answer(int id, byte[] frame)
    {
        var answer = await Run(id, frame).ConfigureAwait(false);
#if RASK_BROWSER
        AnswerImport(id, answer);
#else
        _ = answer;
#endif
    }

#if RASK_BROWSER
    [JSImport("answer", "rask-worker")]
    private static partial void AnswerImport(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> frame);
#endif

#if RASK_BROWSER
    [JSExport]
#endif
    public static void Cancel(int id) => _worker?.Cancel(id);

    // What a handler in the worker may ask for: Rask.Cqrs, logging to the console, and an HttpClient on the page's own
    // origin — fetch works in a worker. Nothing the app's Program.cs registers is here, since Program.cs never runs.
    internal static ServiceProvider Services(string baseAddress)
    {
        var services = new ServiceCollection();
        BrowserConsoleLoggerProvider.AddIfNoneRegistered(services);
        services.AddLogging();
        if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var origin))
        {
            services.AddSingleton(new HttpClient { BaseAddress = origin });
        }

        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }

    private static CultureInfo? Culture(string name)
    {
        try
        {
            return string.IsNullOrEmpty(name) ? null : CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
