using System.Net.WebSockets;
using System.Text;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server;

internal sealed class LiveSession : IDisposable, IAsyncDisposable, IRenderHandle
{
    private static readonly AsyncLocal<bool> _inHandlerScope = new();

    private WebSocket? _socket;
    private CancellationToken _socketCt;

    public bool SuppressEventsUntilReconnect { get; set; }

    public LiveSession(string id, Component view, IServiceScope scope)
    {
        Id = id;
        View = view;
        Scope = scope;
        view.RenderHandle = this;
    }

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
        Lock.Dispose();
        Scope.Dispose();
    }

    public void Dispose()
    {
        ComponentLifecycle.DisposeComponentTree(View);
        Lock.Dispose();
        Scope.Dispose();
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
        var payload = LivePayload.BuildPayload(body, historyUrl, replace, cssText: null, auth: auth);
        await _socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, _socketCt)
            .ConfigureAwait(false);
    }
}
