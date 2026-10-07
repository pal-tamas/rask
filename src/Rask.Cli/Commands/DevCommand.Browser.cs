using static Rask.Cli.Commands.DotnetWatchInvocation;

namespace Rask.Cli.Commands;

internal sealed partial class DevCommand
{
    // Which URL, if any, we should open ourselves. Null when nobody should, and null when watch is
    // already going to — the two reasons are spelled out at the branches below.
    internal string? ResolveBrowserOpen(DevTarget target, bool open, bool noOpen, string? urls, string? devHostUrl = null)
    {
        if (noOpen)
        {
            return null;
        }

        // A dev host opens without being asked. `--open` is opt-in on a plain localhost run because the
        // URL there is a guess — a port out of a launch profile that may not be the one you want. The
        // `.test` name is not a guess: we set the machine up for it, we issued the certificate that
        // covers it, and nothing else on the machine answers to it. Making you pass a flag to reach the
        // address the command just spent a password configuring is the wrong default.
        if (!open && devHostUrl is null)
        {
            return null;
        }

        if (target.ProfileLaunchesBrowser)
        {
            // dotnet watch honours launchBrowser itself and neither .NET 10 nor 11 ships an environment
            // variable to suppress it (only DOTNET_WATCH_SUPPRESS_EMOJIS), so opening as well is the one
            // thing we must not do — it would produce two tabs every single run.
            Console.WriteLine(
                devHostUrl is null
                    ? "launchSettings.json already opens a browser for this profile — skipping --open. " +
                      "Set \"launchBrowser\": false there to change that."
                    : $"launchSettings.json opens this profile's own URL, so {devHostUrl} is not what you will land on. " +
                      "Set \"launchBrowser\": false there to open the name instead.",
                ConsoleStyle.Dim);
            return null;
        }

        // With an npm front end the browser belongs on the DEV SERVER, not on ASP.NET: it is what
        // serves the app and what HMR reaches, and it proxies the wire back to the host. Opening the
        // host's own port instead lands on "nothing built yet" and looks like a broken scaffold.
        //
        // --urls is still honoured: it names where the HOST listens, and someone who set it deliberately is
        // saying that is the address they mean.
        if (target.Kind == DevTemplateKind.SpaHosted && urls is not { Length: > 0 })
        {
            return target.ClientDevServerUrl ?? ViteDevServerUrl;
        }

        var url = devHostUrl ?? FirstUrl(urls) ?? target.LaunchUrl;
        if (url is null)
        {
            Console.WriteLine("--open: no URL to open (no launch profile and no --urls).", ConsoleStyle.Dim);
        }

        return url;
    }

    private async Task OpenWhenListeningAsync(string url, CancellationToken cancellationToken)
    {
        // Poll until something answers, then open exactly once. Never fails the run.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    await _browser.TryOpenAsync(url, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Not listening yet, or too slow to answer within the poll's timeout.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
