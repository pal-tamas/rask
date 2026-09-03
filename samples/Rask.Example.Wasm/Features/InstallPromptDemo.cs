using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IInstallPrompt" /> — show a custom "Install app" button driven by the deferred
///     <c>beforeinstallprompt</c> event, then trigger the prompt from the click. The button only appears
///     once the browser deems the app installable (valid manifest + service worker over HTTPS) and it
///     isn't already installed.
/// </summary>
public sealed partial class InstallPromptDemo(IInstallPrompt install) : Component
{
    private bool _canInstall;
    private bool _installed;
    private string _status = "checking…";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            _installed = await install.IsInstalledAsync();
            _canInstall = !_installed && await install.CanInstallAsync();
            _status = _installed
                ? "running as an installed app"
                : _canInstall
                    ? "installable — use the button"
                    : "not installable yet (needs HTTPS + manifest + service worker, fired once per load)";
        }
        catch (Exception ex)
        {
            _status = "check failed: " + ex.Message;
        }

        StateHasChanged();
    }

    private async Task Install()
    {
        try
        {
            var outcome = await install.PromptAsync();
            _status = $"prompt outcome: {outcome}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status = "prompt failed: " + ex.Message;
        }
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button
                        .Class(Tw.BtnPrimary)
                        .Id("install-button")
                        .Disabled(!_canInstall)
                        .OnClickAsync(Install)[
                        Icon.Name(IconName.Download).Class("me-1"), "Install app"],
                    Button
                        .Class(Tw.BtnOutlineSecondary)
                        .Id("install-refresh")
                        .OnClickAsync(RefreshAsync)[
                        "Re-check"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("install-status")[_status]]
            ]
        ];
}
