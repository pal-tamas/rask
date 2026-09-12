using System.Text.Json;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.DevTools.Panel;

/// <summary>
///     The devtools panel as a live session inside the app's own runtime, for a host with no server to run it on.
/// </summary>
/// <remarks>
///     <para>
///         A second session beside the app's, and deliberately not the WASM host's session type: that one binds the page's
///         JS runtime and interop bridge, which belong to the app. This one owns neither. Its frames go to a delegate — the
///         drawer's frame on a WASM page, a list in a test — and its events arrive through <see cref="DispatchAsync" />.
///     </para>
///     <para>
///         Renders serialize on its own lock, and every build yields before it walks, so a panel render never runs inside
///         the app's render walk and the render state the two sessions share per thread is never interleaved.
///     </para>
/// </remarks>
internal sealed class DevToolsPanelSession : LiveSessionBase, IDisposable
{
    /// <summary>The panel document's <c>data-rask-root</c> value.</summary>
    internal const string RootId = "rask-devtools";

    // How many extra builds one render may take for StateHasChanged calls raised while it builds — the WASM host's budget.
    private const int RebuildBudget = 2;

    private readonly Func<ReadOnlyMemory<byte>, ValueTask> _send;

    // Not disposed with the session: a render request can race disposal, and a disposed semaphore would turn that race
    // into an exception on a thread-pool continuation. Without AvailableWaitHandle it holds nothing to release.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool _pendingRenderInScope;
    private bool _lastBuildHadJsInvokes;
    private int _disposed;

    /// <param name="view">The panel's root, already wrapped in the root error boundary.</param>
    /// <param name="services">The panel's own container — never the app's, whose route state and navigator are the app's.</param>
    /// <param name="send">Where each frame goes. The memory is valid only for the call.</param>
    internal DevToolsPanelSession(Component view, IServiceProvider services, Func<ReadOnlyMemory<byte>, ValueTask> send)
        : base(view, services, LiveDiffMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Renders the panel for the first time and sends the whole document.</summary>
    internal async Task InitialRenderAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await BuildCoalescingAsync(publishOnly: false).ConfigureAwait(false);
            if (await TryEmitFrameAsync(force: true).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    /// <summary>One event from the panel frame: the handler runs, and the panel re-renders if anything changed.</summary>
    internal async Task DispatchAsync(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || IsDisposed)
        {
            return;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || idElement.GetString() is not { Length: > 0 } handlerId)
        {
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            if (!await View.TryInvokeHandlerAsync(handlerId, root, Services).ConfigureAwait(false))
            {
                return;
            }

            await BuildCoalescingAsync(publishOnly: false).ConfigureAwait(false);
            if (await TryEmitFrameAsync(force: false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
#pragma warning disable CA1031 // A panel handler that throws must not take the app's runtime down with it: reported, not rethrown.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.DevTools", $"Rask DevTools panel handler '{handlerId}' threw", ex);
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // The culture service is the app's singleton and outlives this session; left subscribed, it would keep the
        // panel's tree reachable and re-render it after it closed.
        DetachCulture();
        ComponentLifecycle.DisposeComponentTree(View);
    }

    /// <inheritdoc />
    protected override ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame) => _send(frame);

    /// <inheritdoc />
    protected override async Task RequestRenderInternalAsync(bool publishOnly)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InHandlerScope)
        {
            // Inside a build already: fold into it rather than queue behind the lock this caller's own render holds.
            _pendingRenderInScope = true;
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        InHandlerScope = true;
        try
        {
            await BuildCoalescingAsync(publishOnly).ConfigureAwait(false);

            // A publish render that changed nothing is not sent: re-applying identical markup costs the frame a morph.
            if (publishOnly && !_lastBuildHadJsInvokes && _htmlBuffers.CurrentEqualsPrevious())
            {
                return;
            }

            if (await TryEmitFrameAsync(force: false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            InHandlerScope = false;
            _lock.Release();
            _ = DrainRenderRequestedAfterScope();
        }
    }

    /// <inheritdoc />
    protected override async Task RenderInScopeCoreAsync()
    {
        // The dispatcher already holds the lock. Built without the handler's synchronization context, whose Post would
        // re-enter this method through the build's yield — the loop the WASM host avoids the same way.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            await BuildPayloadAsync(publishOnly: false, commitCache: true).ConfigureAwait(false);
            if (await TryEmitFrameAsync(force: false).ConfigureAwait(false))
            {
                _htmlBuffers.Commit();
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private Task DrainRenderRequestedAfterScope()
    {
        if (!_pendingRenderInScope || IsDisposed)
        {
            return Task.CompletedTask;
        }

        _pendingRenderInScope = false;
        return RequestPublishRenderAsync();
    }

    // Every build diffs against the last SENT render; only the final one, which is the one sent, becomes the baseline.
    private async Task BuildCoalescingAsync(bool publishOnly)
    {
        _pendingRenderInScope = false;
        await BuildPayloadAsync(publishOnly, commitCache: false).ConfigureAwait(false);
        var budget = RebuildBudget;
        while (_pendingRenderInScope && budget-- > 0)
        {
            _pendingRenderInScope = false;
            await BuildPayloadAsync(publishOnly, commitCache: false).ConfigureAwait(false);
        }

        _renderCache?.Snapshot();

        // Dropped on purpose once the budget is spent, as the hosts do: carrying it would loop a component that asks for a
        // render from every render.
        _pendingRenderInScope = false;
    }

    private async Task BuildPayloadAsync(bool publishOnly, bool commitCache)
    {
        // Off the caller's stack first, so the walk below never nests inside another session's walk on this thread.
        await Task.Yield();

        var html = RenderTreeToHtml(publishOnly, out var frameWriter);
        var download = ConsumeDownload();
        var jsInvokes = JsInvokes.Drain();
        _lastBuildHadJsInvokes = jsInvokes is not null;
        WritePayload(html, frameWriter, download, jsInvokes, historyUrl: null, replace: false, commitCache, auth: null, RootId);
    }
}
