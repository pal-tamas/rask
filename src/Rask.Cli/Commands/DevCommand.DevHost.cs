using Rask.Cli.Dev;

namespace Rask.Cli.Commands;

internal sealed partial class DevCommand
{
    /// <summary>
    ///     Set the machine up to serve this project on <c>https://&lt;name&gt;.test</c>, or return null to
    ///     use the ordinary localhost URLs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only where Kestrel is what the browser talks to: a standalone WebAssembly app is served by the
    ///         SDK's own dev server, so a certificate on an ASP.NET host is not one the browser would ever see.
    ///     </para>
    ///     <para>
    ///         Skipped whenever <c>--urls</c> is given: someone naming the addresses to listen on is
    ///         being explicit, and quietly serving somewhere else would be the opposite of helpful.
    ///     </para>
    /// </remarks>
    private async Task<DevHostResult?> TryPrepareDevHostAsync(
        DevTarget target,
        ParsedArguments parsed,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        if (parsed.HasFlag("no-host")
            || parsed.Option("urls") is { Length: > 0 }
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RASK_DEV_NO_HOST"))
            || !DevHostSetup.IsSupported)
        {
            return null;
        }

        if (target.Kind is not (DevTemplateKind.Server or DevTemplateKind.WasmHosted or DevTemplateKind.Unknown))
        {
            return null;
        }

        var setup = new DevHostSetup(Console, _process, new DevHostStore(DevHostStore.DefaultRoot));
        return await setup.TryPrepareAsync(target.Name, !nonInteractive, cancellationToken).ConfigureAwait(false);
    }
}
