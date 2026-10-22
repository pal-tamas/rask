using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Server.JSInterop;

/// <summary>
///     The live session's JS runtime as an event's scope hands it out: the same connection, and nothing the event's
///     scope can dispose with it.
/// </summary>
internal sealed class ForwardedJSRuntime(IJSRuntime session) : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(
        string identifier, object?[]? args) =>
        session.InvokeAsync<TValue>(identifier, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args) =>
        session.InvokeAsync<TValue>(identifier, cancellationToken, args);
}
