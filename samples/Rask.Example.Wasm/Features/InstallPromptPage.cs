using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="InstallPromptDemo" /> (<c>IInstallPrompt</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("install")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class InstallPromptPage : Component
{
    protected override Component? Head => Title()["Install prompt — Rask"];

    protected override Component? Render() =>
    [
        H1(Class: "h2 mb-1")["Install prompt"],
        P(Class: "text-secondary")[
            "Show a custom \"Install app\" button via IInstallPrompt instead of the browser's default ",
            "mini-infobar. The framework captures and defers the beforeinstallprompt event at boot, so you ",
            "reveal your button when CanInstallAsync() is true and trigger PromptAsync() from the click. ",
            "WASM-only — the install flow needs the live document and transient activation."
        ],
        CodeSample(
            ["InstallPromptDemo.cs"],
            Notes: "CanInstallAsync()/IsInstalledAsync() are one-shot polls; PromptAsync() replays the deferred "
                + "event and returns the user's InstallOutcome. The browser only offers it over HTTPS with a "
                + "valid manifest + service worker, once per load.",
            Result: InstallPromptDemo())
    ];
}
