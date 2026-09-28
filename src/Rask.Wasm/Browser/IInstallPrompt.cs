using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the PWA install prompt (the <c>beforeinstallprompt</c> event,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/BeforeInstallPromptEvent" />) — show
///     your own "Install app" button instead of relying on the browser's default mini-infobar. The
///     framework captures and defers the browser's install event at boot, so you can trigger it later from
///     a user gesture. <b>WASM-only:</b> the install flow needs the live document and transient activation
///     that the Server/WebSocket transport can't carry; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Typical flow: poll <see cref="CanInstallAsync" /> after first render and reveal your install
///         button when it's <c>true</c>; in the button's click handler call <see cref="PromptAsync" />. The
///         browser only fires <c>beforeinstallprompt</c> when its install criteria are met (a valid
///         manifest, a service worker, HTTPS), and only once per page load.
///     </para>
///     <para>
///         Use <see cref="IsInstalledAsync" /> (display-mode / iOS <c>navigator.standalone</c> check) to
///         hide install affordances when the app is already running as an installed PWA.
///     </para>
/// </remarks>
public interface IInstallPrompt
{
    /// <summary>Whether a deferred install prompt is available to show (the browser fired and we captured it).</summary>
    ValueTask<bool> CanInstallAsync();

    /// <summary>
    ///     Shows the captured install prompt and resolves to the user's choice. Returns
    ///     <see cref="InstallOutcome.Unavailable" /> when no prompt is pending. Call from a user gesture; the
    ///     prompt is consumed (one-shot) after showing.
    /// </summary>
    ValueTask<InstallOutcome> PromptAsync();

    /// <summary>Whether the app is currently running as an installed PWA (standalone display mode).</summary>
    ValueTask<bool> IsInstalledAsync();
}
