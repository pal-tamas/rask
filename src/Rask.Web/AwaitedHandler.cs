using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.ScopedAssets;

namespace Rask.Web;

/// <summary>
///     A C# handler whose promise the browser waits on (a lock request's), on its way to the browser.
/// </summary>
/// <remarks>
///     One a component owns is registered at once and runs in its component, re-rendering it, as any handler does, until
///     the component unmounts. One that belongs to no component — a hosted service's, which has nothing to re-render —
///     is registered when the call runs and let go when it settles: a lock request settles only once its handler has,
///     so that is exactly as long as the browser can call it.
/// </remarks>
internal sealed class AwaitedHandler
{
    private readonly Component? _owner;
    private readonly Func<JsonElement, Task> _fromBrowser;
    private readonly Func<object?[], Task> _fromFake;

    private AwaitedHandler(Delegate handler, Func<JsonElement, Task> fromBrowser, Func<object?[], Task> fromFake)
    {
        Handler = handler;
        _owner = DelegateOwner.Resolve(handler);
        _fromBrowser = fromBrowser;
        _fromFake = fromFake;
        Owned = _owner is null ? null : ScopedScript.Handler(_owner, fromBrowser, awaited: true);
    }

    /// <summary>What the code under test handed over: a fake's <see cref="WebCall" /> records it.</summary>
    public Delegate Handler { get; }

    /// <summary>The registration of a handler a component owns; null for one that belongs to none.</summary>
    public ScopedScript.ScriptCallback? Owned { get; }

    // `fromBrowser` reads the arguments the browser calls it with; `fromFake` takes the ones a test's fake hands it.
    public static AwaitedHandler Of(Delegate handler, Func<JsonElement, Task> fromBrowser, Func<object?[], Task> fromFake) =>
        new(handler, fromBrowser, fromFake);

    // For one run: the caller releases it when the run settles.
    public ScopedScript.ScriptCallback Register(IJSRuntime runtime) => ScopedScript.Handler(runtime, _fromBrowser);

    // What a fake does in the browser's place: runs it with `args`, in its component when it has one.
    public Task Run(object?[] args) => _owner is null ? _fromFake(args) : _owner.RunFromScriptNow(() => _fromFake(args));

    // The first argument a fake hands the handler, as the type it takes.
    public static T Given<T>(object?[] args) => args switch
    {
        [] or [null, ..] => default!,
        [T value, ..] => value,
        [var other, ..] => throw new InvalidOperationException(
            $"The fake calls back with a {other.GetType().Name}, but the handler takes a {typeof(T).Name}."),
    };
}
