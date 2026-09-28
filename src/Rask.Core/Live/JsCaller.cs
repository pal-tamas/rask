using Microsoft.JSInterop;

namespace Rask.Core.Live;

/// <summary>
///     Which runtime is calling into .NET right now. The Server host names the session's runtime around each
///     <c>dotNetInvoke</c> it dispatches; <c>DotNetDispatcher</c> runs the target method synchronously on that
///     thread, so the lookup inside it sees the caller.
/// </summary>
internal static class JsCaller
{
    [ThreadStatic] private static IJSRuntime? _current;

    public static Scope Enter(IJSRuntime runtime)
    {
        var previous = _current;
        _current = runtime;
        return new Scope(previous);
    }

    // No caller named means a process with one user behind it (the browser host) or a direct call from
    // .NET; either way there is no other session to protect.
    public static bool Owns(IJSRuntime? owner) => _current is not { } caller || ReferenceEquals(caller, owner);

    public readonly struct Scope(IJSRuntime? previous) : IDisposable
    {
        public void Dispose() => Restore(previous);

        private static void Restore(IJSRuntime? runtime) => _current = runtime;
    }
}
