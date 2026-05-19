namespace Rask.Core.Live;

/// <summary>
///     One queued invocation of a named function on a scoped-JS module. Queued from
///     <see cref="Component.InvokeJs(string)"/> /
///     <see cref="Component.InvokeJs(string, object?[])"/> (fire-and-forget) or
///     <see cref="Component.InvokeJsAsync{T}(string, object?[])"/> (round-trip with
///     return value) inside a C# lifecycle hook (typically <c>OnRendered</c>);
///     serialised into the WS / interop payload and dispatched against every DOM
///     element whose <c>data-rask-mount</c> matches <see cref="ScopeId"/>.
///     <para>
///     When <see cref="InvokeId"/> is non-null the host expects the client to ship
///     the return value back keyed by that id; the host's
///     <see cref="JsInvokeResultStore"/> completes the awaiting
///     <see cref="TaskCompletionSource{T}"/>. Fire-and-forget invocations carry a
///     null id and discard whatever the function returns.
///     </para>
/// </summary>
public readonly record struct ScopedJsInvoke(string ScopeId, string Method, object?[]? Args, int? InvokeId);
