using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="INavigatorInfo" />, backed by the unified <see cref="IJSRuntime" />. Each
///     property is read by its dotted identifier — the client returns the value when it isn't a function.
/// </summary>
public sealed class NavigatorInfo(IJSRuntime js) : INavigatorInfo
{
    /// <inheritdoc />
    public ValueTask<bool> OnLineAsync() => js.InvokeAsync<bool>("navigator.onLine");

    /// <inheritdoc />
    public ValueTask<string> LanguageAsync() => js.InvokeAsync<string>("navigator.language");

    /// <inheritdoc />
    public ValueTask<string> UserAgentAsync() => js.InvokeAsync<string>("navigator.userAgent");
}
