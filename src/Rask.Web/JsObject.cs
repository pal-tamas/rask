namespace Rask.Web;

/// <summary>
///     A live object in the browser, as one of MDN's interfaces (the generated types in <c>Rask.Web.Types</c>). Until it is
///     kept it is a path — <c>Navigator.Clipboard</c> — and each member runs that path in one round trip. Awaiting it keeps
///     it: the browser holds the object for you until you dispose of it.
/// </summary>
/// <example>
///     <code>
///     var wide = await Window.MatchMedia("(min-width: 800px)").Matches;   // one round trip, nothing kept
///     await using var mql = await Window.MatchMedia("(min-width: 800px)"); // kept: the same object each time
///     </code>
/// </example>
public abstract class JsObject : IAsyncDisposable
{
    private protected JsObject(JsChain chain) => Chain = chain;

    /// <summary>The path to this object, or the object the browser holds for you.</summary>
    internal JsChain Chain { get; }

    /// <summary>Whether this browser has it: the path to it ends at something.</summary>
    public ValueTask<bool> IsSupported => Chain.Exists();

    /// <summary>Lets the browser drop a kept object. A path holds nothing, so disposing of one does nothing.</summary>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Chain.Release();
    }

    // The same interface over another chain: what keeping it returns.
    private protected abstract JsObject With(JsChain chain);

    internal async ValueTask<T> Keep<T>()
        where T : JsObject =>
        Chain.IsKept ? (T)this : (T)With(await Chain.Keep().ConfigureAwait(false));
}
