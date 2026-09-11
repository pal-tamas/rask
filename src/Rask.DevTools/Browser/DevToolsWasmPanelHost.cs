using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Browser;

/// <summary>
///     The devtools on a WASM page: puts the pill on the page once the app has rendered, and starts the panel session the
///     first time the drawer opens.
/// </summary>
/// <remarks>
///     A hosted service because the WASM host starts those last, after the app's first render — the pill belongs on a page
///     that is already there. The panel session is not started until it is wanted: a page whose tools stay closed never
///     renders the panel, and never pays its Wire tab's refreshes.
/// </remarks>
internal sealed class DevToolsWasmPanelHost(DevToolsFeeds feeds) : IHostedService, IDisposable
{
    private ServiceProvider? _panelServices;
    private DevToolsPanelSession? _session;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!DevToolsUiKit.IsAvailable())
        {
            DevToolsUiKit.ReportMissing();
            return;
        }

        try
        {
            await DevToolsWasmInterop.ImportAsync(DevToolsBrowserScripts.LoadModule()).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A dev tool that cannot load must say why and leave the app running.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning,
                "Rask.DevTools",
                "Rask DevTools could not load their script into this page. A Content-Security-Policy that forbids data: "
                + "scripts blocks it; allow them in development to use the tools.",
                ex);
            return;
        }

        DevToolsWasmInterop.Install(DevToolsBrowserScripts.FrameDocument(), Open, OnEvent);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        _panelServices?.Dispose();
        _panelServices = null;
    }

    private void Open()
    {
        if (_session is not null)
        {
            return;
        }

        // The panel's own container. The app's is one root of singletons — its route state, its navigator, its download
        // sink — and a panel sharing them would move the app's route when it rendered and take the app's downloads.
        var services = new ServiceCollection();
        services.AddSingleton(new RouteState { Path = DevToolsProbe.PanelPrefix + "/" });
        services.AddSingleton<Navigator>();
        services.AddSingleton(feeds);
        services.AddSingleton<IDevToolsInspection, DevToolsBrowserInspection>();
        _panelServices = services.BuildServiceProvider();

#pragma warning disable RASK014 // The root has no parent render context to construct it through, as on both hosts.
        var root = new RootErrorBoundary(ActivatorUtilities.CreateInstance<DevToolsShell>(_panelServices));
#pragma warning restore RASK014

        _session = new DevToolsPanelSession(root, _panelServices, static frame =>
        {
            // A writable view only because the marshaller's memory view takes one, as the WASM host's own frame push does:
            // the module copies the bytes out and never writes to them.
            DevToolsWasmInterop.Deliver(System.Runtime.InteropServices.MemoryMarshal.AsMemory(frame).Span);
            return ValueTask.CompletedTask;
        });
        _ = _session.InitialRenderAsync();
    }

    private void OnEvent(string json) => _ = _session?.DispatchAsync(Encoding.UTF8.GetBytes(json));
}
