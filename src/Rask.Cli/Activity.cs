using Spectre.Console;

namespace Rask.Cli;

/// <summary>
/// Progress feedback for otherwise-silent long operations (e.g. polling a remote container until it's
/// healthy). On a terminal this is a Spectre status display: an animated line that owns the bottom of
/// the screen while the work runs and clears itself afterwards.
/// </summary>
internal static class Activity
{
    /// <summary>
    /// Run <paramref name="work"/> while showing <paramref name="message"/>.
    /// <para>
    /// <b>When stdout is redirected the work simply runs — not one byte is written.</b> Piped output and
    /// captured test output stay byte-for-byte identical to a run with no progress feedback at all, and
    /// CI logs don't fill with animation frames. Spectre's own non-interactive fallback still prints the
    /// message once, so the guard here is deliberate rather than redundant.
    /// </para>
    /// </summary>
    public static async Task<T> RunAsync<T>(IConsole console, string message, Func<Task<T>> work)
    {
        if (console.IsOutputRedirected)
        {
            return await work().ConfigureAwait(false);
        }

        var result = default(T)!;
        await console.Ansi.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async _ => result = await work().ConfigureAwait(false))
            .ConfigureAwait(false);

        return result;
    }
}
