using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server;

internal sealed class LiveSession : IDisposable, IAsyncDisposable, IRenderHandle
{
    private static readonly AsyncLocal<bool> _inHandlerScope = new();

    private string? _lastSentPayload;
    private WebSocket? _socket;
    private CancellationToken _socketCt;

    public LiveSession(string id, Component view, IServiceScope scope)
    {
        Id = id;
        View = view;
        Scope = scope;
        view.RenderHandle = this;
        // RootErrorBoundary wraps the user's App; forward the handle to the inner so its
        // StateHasChanged() still reaches the session even before the first GetOrCreate
        // (which would otherwise be where the handle gets lazily attached).
        if (view is RootErrorBoundary root)
        {
            root.Inner.RenderHandle = this;
        }
    }

    public bool SuppressEventsUntilReconnect { get; set; }

    public string Id { get; }
    public Component View { get; }
    public IServiceScope Scope { get; }
    public IServiceProvider Services => Scope.ServiceProvider;
    public SemaphoreSlim Lock { get; } = new(1, 1);

    public bool InHandlerScope
    {
        get => _inHandlerScope.Value;
        set => _inHandlerScope.Value = value;
    }

    public async ValueTask DisposeAsync()
    {
        await ComponentLifecycle.DisposeComponentTreeAsync(View).ConfigureAwait(false);
        ReleaseFileStores();
        Lock.Dispose();
        Scope.Dispose();
    }

    public void Dispose()
    {
        ComponentLifecycle.DisposeComponentTree(View);
        ReleaseFileStores();
        Lock.Dispose();
        Scope.Dispose();
    }

    private void ReleaseFileStores()
    {
        try
        {
            Services.GetService<Files.SessionUploadStore>()?.ReleaseSession(Id);
            Services.GetService<Files.SessionDownloadStore>()?.ReleaseSession(Id);
        }
        catch
        {
            // best-effort cleanup; do not let store errors mask disposal
        }
    }

    public async Task RequestRenderAsync()
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        if (InHandlerScope)
        {
            await RenderAndSendAsync(null, false).ConfigureAwait(false);
            return;
        }

        await Lock.WaitAsync(_socketCt).ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await RenderAndSendAsync(null, false).ConfigureAwait(false);
        }
        finally
        {
            InHandlerScope = false;
            Lock.Release();
        }
    }

    Task IRenderHandle.RenderInScopeAsync() => RenderAndSendAsync(null, false);

    public void AttachSocket(WebSocket socket, CancellationToken ct)
    {
        _socket = socket;
        _socketCt = ct;
        SuppressEventsUntilReconnect = false;
        // A reconnect — possibly a different browser tab/window — needs the current HTML
        // even when it byte-matches the prior socket's last frame. Reset the dedup baseline
        // so the recovery render reliably emits.
        _lastSentPayload = null;
    }

    public void DetachSocket()
    {
        _socket = null;
        _socketCt = default;
    }

    internal async Task RenderAndSendAsync(string? historyUrl, bool replace, AuthInstruction? auth = null)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
        {
            return;
        }

        var html = View.RenderAsLiveRoot(Services);
        var withRoot = LivePayload.InjectRootAttr(html, Id);
        var body = LivePayload.ExtractBody(withRoot);

        PendingDownload? download = null;
        if (Services.GetService<IDownloadSink>() is { } sink && sink.TryConsume(out var pd))
        {
            download = pd;
        }

        var payload = LivePayload.BuildPayload(body, historyUrl, replace, null, auth, download);

        // Skip the frame when the payload is byte-identical to the previous one AND nothing
        // out-of-band (navigation, auth instruction) needs to flow. Catches handler invocations
        // that ended up not modifying tracked state.
        if (historyUrl is null && auth is null && download is null
            && string.Equals(payload, _lastSentPayload, StringComparison.Ordinal))
        {
            return;
        }

        await _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, _socketCt)
            .ConfigureAwait(false);
        _lastSentPayload = payload;
    }
}
