using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rask.Cqrs;

namespace Rask.Wasm;

// Points the app's [OffThread] messages at a worker, on a page in a browser. Anywhere else — the build's prerender pass,
// this package's own tests — there is no worker, and the handlers run where they are, as they always did.
internal static class OffThreadWiring
{
    internal static void Add(IServiceCollection services, OffThreadOptions options)
    {
        if (!OperatingSystem.IsBrowser() || WasmPrerender.RequestedOutput is { Length: > 0 })
        {
            return;
        }

        services.TryAddSingleton(static sp =>
            new OffThreadDispatch(sp, BrowserOffThreadChannel.Instance, OffThreadDispatch.DefaultGrace));
        OffThreadInvokers.Install();

        if (options.Warm)
        {
            services.AddHostedService<Warming>();
        }
    }

    // Started with the app's other hosted services, after the first page is on screen: a starting runtime competes with
    // the page's for the network and the cores, and the first paint must not wait behind it.
    private sealed class Warming(OffThreadDispatch dispatch) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            dispatch.Channel.Warm();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
