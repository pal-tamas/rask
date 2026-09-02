using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Blazor;

/// <summary>
///     The <see cref="IJSRuntime" /> a statically rendered island hands its hosted component.
/// </summary>
/// <remarks>
///     Throws rather than no-ops, deliberately. A hosted component calling into JavaScript has hit a
///     real capability gap — there is no browser-side renderer for it to talk through — and a silent
///     no-op would leave the component looking correct while being subtly wrong, which is the failure
///     shape this package works hardest to avoid. The message names the component and what to do.
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
        new($"A hosted Blazor component called the JavaScript function '{identifier}', which a "
            + "server-rendered island cannot do: its markup is produced on the server and there is no "
            + "browser-side Blazor renderer to call through. Use a component that renders without "
            + "JavaScript, or keep the interactive part in Rask — a Rask child placed inside the "
            + "island keeps its own working handlers. See docs/blazor-components.md.");
}
