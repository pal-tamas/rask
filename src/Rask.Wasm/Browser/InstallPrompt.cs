using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IInstallPrompt" />, backed by the unified <see cref="IJSRuntime" />. The
///     framework's <c>__raskInstall</c> helper listens for <c>beforeinstallprompt</c> at boot, calls
///     <c>preventDefault()</c>, and holds the event so <see cref="PromptAsync" /> can replay it later.
/// </summary>
public sealed class InstallPrompt(IJSRuntime js) : IInstallPrompt
{
    /// <inheritdoc />
    public ValueTask<bool> CanInstallAsync() => js.InvokeAsync<bool>("__raskInstall.canInstall");

    /// <inheritdoc />
    public async ValueTask<InstallOutcome> PromptAsync() =>
        await js.InvokeAsync<string>("__raskInstall.prompt").ConfigureAwait(false) switch
        {
            "accepted" => InstallOutcome.Accepted,
            "dismissed" => InstallOutcome.Dismissed,
            _ => InstallOutcome.Unavailable
        };

    /// <inheritdoc />
    public ValueTask<bool> IsInstalledAsync() => js.InvokeAsync<bool>("__raskInstall.isInstalled");
}
