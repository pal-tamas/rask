using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Core.ScopedAssets;

/// <summary>
///     Infrastructure. The base of the proxy Rask generates for a class a component's scoped TypeScript
///     exports; each exported method becomes a typed method on the proxy. Disposed with its component.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ScriptObject : IAsyncDisposable
{
    private int _disposed;

    /// <summary>Wraps the browser-side instance.</summary>
    protected ScriptObject(IJSObjectReference reference) => Reference = reference;

    /// <summary>The browser-side instance — what an export taking this class is handed.</summary>
    public IJSObjectReference Reference { get; }

    internal Component? Owner { get; set; }

    internal CancellationTokenRegistration Release { get; set; }

    /// <summary>Releases the browser-side instance. Runs by itself when the owning component unmounts.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Release.Unregister();
        try
        {
            await Reference.DisposeAsync().ConfigureAwait(false);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the instance with it.
        }
        catch (JSException)
        {
            // The script already dropped or broke the instance; there is nothing left to release.
        }

        GC.SuppressFinalize(this);
    }

    // Named for the script rather than plainly (`Call`), because the generated proxy's own methods are
    // the class's method names, PascalCased — a TS `call()` must not collide with the machinery.

    /// <summary>Calls a method that returns nothing.</summary>
    protected ValueTask CallScript(string method, params object?[] args) => Reference.InvokeVoidAsync(method, args);

    /// <summary>Calls a method and reads its result as <typeparamref name="T" />.</summary>
    protected ValueTask<T> CallScript<[DynamicallyAccessedMembers(ScopedScript.JsonSerialized)] T>(
        string method, params object?[] args) => Reference.InvokeAsync<T>(method, args);

    /// <summary>Calls a method that returns a tuple — see <see cref="ScopedScript.Tuple{T}" />.</summary>
    protected async ValueTask<T> CallScriptTuple<T>(
        string method, Func<JsonElement, T> read, params object?[] args)
    {
        var array = await Reference.InvokeAsync<JsonElement>(method, args).ConfigureAwait(false);
        return read(array);
    }

    /// <summary>Calls a method that returns another exported class's instance.</summary>
    protected async ValueTask<T> CallScriptObject<T>(
        string method, Func<IJSObjectReference, T> wrap, params object?[] args)
        where T : ScriptObject
    {
        var reference = await Reference.InvokeAsync<IJSObjectReference>(method, args).ConfigureAwait(false);
        var value = wrap(reference);
        return Owner is { } owner ? ScopedScript.Adopt(owner, value) : value;
    }

    /// <summary>Hands a parameterless callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback(Callback callback) => ScopedScript.Callback(RequireOwner(), callback);

    /// <summary>Hands a one-argument callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback<T>(Callback<T> callback) => ScopedScript.Callback(RequireOwner(), callback);

    /// <summary>Hands a two-argument callback to a method, owned by this instance's component.</summary>
    protected object ScriptCallback<T1, T2>(Callback<T1, T2> callback) =>
        ScopedScript.Callback(RequireOwner(), callback);

    private Component RequireOwner() =>
        Owner ?? throw new InvalidOperationException(
            $"{GetType().Name} was not created by its component, so it has no component to run a callback on.");
}
