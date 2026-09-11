using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;
using Rask.Wasm;

namespace Rask.DevTools;

internal sealed partial class DevToolsBootstrap
{
    // The browser face. The loader runs it after rask.wasm.js is imported and before the container is built, which is
    // the one moment the page's origin is readable and the first render is still ahead — so a probe installed here sees
    // the page from its first frame, the way the Server host's does.
    static partial void AttachHost(IServiceCollection services)
    {
        if (!DevToolsOrigin.IsLoopback(WasmHostBuilder.BaseAddress))
        {
            return;
        }

        // Instances, not types: the hook and the container must hold the same probe and feeds, and the container does
        // not exist yet. Registered before the shared registrations, which only TryAdd.
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);
        services.AddSingleton(feeds);
        services.AddSingleton(probe);
        RaskDevToolsHook.Probe = probe;
    }
}
