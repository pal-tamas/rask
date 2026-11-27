using Microsoft.Extensions.DependencyInjection;

namespace Rask;

/// <summary>
///     The commands a <see cref="RaskApp" /> runs instead of its server when started as <c>dotnet App.dll rask …</c> —
///     against its own database, with its own model and rules, so they need nothing but the published app:
///     <c>rask db migrate</c>.
/// </summary>
/// <remarks>
///     <para>
///         The host is built and never started: no port is bound, no worker runs, so a command can run beside the app
///         serving the same database — or as a one-off container that migrates before a release starts. The exit code is
///         the answer — 0 done, 1 the command failed (the reason on standard error, nothing half-applied: each migration
///         is its own transaction), 2 the command line was wrong.
///     </para>
/// </remarks>
internal static class AppCommand
{
    private const int Done = 0;
    private const int Failed = 1;
    private const int Usage = 2;

    private const string Help =
        "Usage: dotnet <App>.dll rask <command>\n"
        + "  rask db migrate   Apply pending migrations, then their data steps.";

    /// <summary>Whether <paramref name="args" /> name a command rather than start the server.</summary>
    internal static bool Names(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "rask", StringComparison.Ordinal);

    /// <summary>Runs the command <paramref name="args" /> name, through the built app's services.</summary>
    /// <param name="services">The built app's services; the host is not started.</param>
    /// <param name="args">The process arguments, <c>rask</c> first.</param>
    /// <param name="error">Where a wrong command line, or a failure, is said.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> RunAsync(
        IServiceProvider services, IReadOnlyList<string> args, TextWriter error, CancellationToken cancellationToken)
    {
        var command = args.Skip(1).ToList();
        switch (command)
        {
            case ["db", "migrate" or "update"]:
                return await MigrateAsync(services, error, cancellationToken).ConfigureAwait(false);
            default:
                var said = command.Count == 0 ? "No command given." : $"Unknown command 'rask {string.Join(' ', command)}'.";
                await Say(error, said + "\n" + Help, cancellationToken).ConfigureAwait(false);
                return Usage;
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider services, TextWriter error, CancellationToken cancellationToken)
    {
        if (services.GetService<IAppMigration>() is not { } migration)
        {
            await Say(error, "rask db migrate needs the app's database: this app has the Data battery off.", cancellationToken).ConfigureAwait(false);
            return Usage;
        }

        try
        {
            await migration.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return Done;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await Say(error, "rask db migrate failed: " + e.Message, cancellationToken).ConfigureAwait(false);
            return Failed;
        }
    }

    private static Task Say(TextWriter writer, string text, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(text.AsMemory(), cancellationToken);
}
