using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Blazor;

/// <summary>
///     The <see cref="IJSRuntime" /> an island hands its hosted component when the app registered none.
/// </summary>
/// <remarks>
///     Throws rather than no-ops, deliberately. Both hosts register a real runtime, so this is reached
///     only by a render nothing hosts — and a silent no-op there would leave the component looking
///     correct while being subtly wrong, which is the failure shape this package works hardest to
///     avoid. The message names the function that was called and what to do.
/// </remarks>
internal sealed class RaskBlazorJSRuntime : IJSRuntime
{
    /// <summary>
    ///     What <see cref="IJSRuntime" /> declares on <c>TValue</c>, repeated verbatim.
    /// </summary>
    /// <remarks>
    ///     An override must carry the SAME <c>DynamicallyAccessedMembers</c> as the member it
    ///     implements or the trim analyser reports IL2095 — which, in a WASM app publishing trimmed
    ///     under warnings-as-errors, is a build error in the consuming app for a method that only ever
    ///     throws. The value is the one JSInterop uses for a JSON-serialized result: the members its
    ///     serializer would need if this implementation ever returned one.
    /// </remarks>
    private const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties;

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier,
        object?[]? args) =>
        throw Unsupported(identifier);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonSerialized)] TValue>(
        string identifier,
        CancellationToken cancellationToken,
        object?[]? args) =>
        throw Unsupported(identifier);

    private static InvalidOperationException Unsupported(string identifier) =>
        new($"A hosted Blazor component called the JavaScript function '{identifier}', but this "
            + "render has no IJSRuntime to call through: the app's services registered none. Rask's "
            + "Server and WebAssembly hosts both register one, so this is a render outside a host — "
            + "register an IJSRuntime in the services the render is given. "
            + "See docs/blazor-components.md.");
}
