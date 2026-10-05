using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>beforeinstallprompt</c> from Rask.Web — show your own "Install app" button, and prompt from its click.
///     The browser fires the event once, as the page loads, before this component is there to hear it: Rask.Web keeps
///     it from boot and hands it to <c>Window.OnBeforeInstallPrompt</c>. The button only lights up once the browser
///     deems the app installable (valid manifest + service worker over HTTPS) and it isn't already installed.
/// </summary>
public sealed partial class InstallPromptDemo : Component
{
    private Types.BeforeInstallPromptEvent? _prompt;
    private IAsyncDisposable? _offered;
    private IAsyncDisposable? _installedNow;
    private bool _installed;
    private string _status = "checking…";

    protected override async Task OnFirstRender()
    {
        try
        {
            _offered = await Window.OnBeforeInstallPrompt(e =>
            {
                _prompt = e;
                _status = "installable — use the button";
            });
            _installedNow = await Window.OnAppInstalled(() =>
            {
                _installed = true;
                _prompt = null;
            });
        }
        catch (JSException ex)
        {
            _status = "check failed: " + ex.Message;
            return;
        }

        await Refresh();
    }

    // A standalone window is the one signal an installed app has on a fresh launch.
    private async Task Refresh()
    {
        try
        {
            _installed = _installed || await Window.MatchMedia("(display-mode: standalone)").Matches;
            _status = (_installed, _prompt is not null) switch
            {
                (true, _) => "running as an installed app",
                (false, true) => "installable — use the button",
                _ => "not installable yet (needs HTTPS + manifest + service worker, fired once per load)",
            };
        }
        catch (JSException ex)
        {
            _status = "check failed: " + ex.Message;
        }
    }

    // The prompt is spent once shown, accepted or not.
    private async Task Install()
    {
        if (_prompt is not { } prompt)
        {
            return;
        }

        try
        {
            var answer = await prompt.Prompt();
            _status = $"prompt outcome: {answer.UserChoice}";
        }
        catch (JSException ex)
        {
            _status = "prompt failed: " + ex.Message;
        }

        _prompt = null;
    }

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary
                        .Id("install-button")
                        .Disabled(_prompt is null || _installed)
                        .OnClick(Install)[Ui.Icon.Name(Ui.IconName.Download), "Install app"],
                    Ui.Button.Outline
                        .Id("install-refresh")
                        .OnClick(Refresh)["Re-check"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("install-status")[_status]]
            ];

    protected override async Task OnUnmount()
    {
        if (_offered is not null)
        {
            await _offered.DisposeAsync();
        }

        if (_installedNow is not null)
        {
            await _installedNow.DisposeAsync();
        }
    }
}
