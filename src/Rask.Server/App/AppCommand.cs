using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask;

/// <summary>
///     The commands a <see cref="RaskApp" /> runs instead of its server when started as <c>dotnet App.dll rask …</c> —
///     against its own database, with its own model and rules, so they need nothing but the published app:
///     <c>rask audit [--aggregate Name]… [--json]</c> and <c>rask db migrate</c>.
/// </summary>
/// <remarks>
///     <para>
///         The host is built and never started: no port is bound, no worker runs, so a command can run beside the app
///         serving the same database. The exit code is the answer — 0 done (an audit: every stored row passes),
///         1 an audit found rows its rules refuse, 2 the command line was wrong.
///     </para>
/// </remarks>
internal static class AppCommand
{
    private const int Done = 0;
    private const int Found = 1;
    private const int Usage = 2;

    private const string Help =
        "Usage: dotnet <App>.dll rask <command>\n"
        + "  rask audit [--aggregate <Name>]... [--json]   Report every stored aggregate its current rules refuse (reads only).\n"
        + "  rask audit --unique-candidates [...]         Report the stored rows each unique index would refuse, before it is migrated.\n"
        + "  rask db migrate                              Apply pending migrations, then their data steps.";

    /// <summary>Whether <paramref name="args" /> name a command rather than start the server.</summary>
    internal static bool Names(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "rask", StringComparison.Ordinal);

    /// <summary>Runs the command <paramref name="args" /> name, through the built app's services.</summary>
    /// <param name="services">The built app's services; the host is not started.</param>
    /// <param name="args">The process arguments, <c>rask</c> first.</param>
    /// <param name="output">Where the answer is written.</param>
    /// <param name="error">Where a wrong command line, or a failure, is said.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> RunAsync(
        IServiceProvider services, IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var command = args.Skip(1).ToList();
        switch (command)
        {
            case ["audit", .. var options]:
                return await AuditAsync(services, options, output, error, cancellationToken).ConfigureAwait(false);
            case ["db", "migrate" or "update"]:
                return await MigrateAsync(services, error, cancellationToken).ConfigureAwait(false);
            default:
                var said = command.Count == 0 ? "No command given." : $"Unknown command 'rask {string.Join(' ', command)}'.";
                await Say(error, said + "\n" + Help, cancellationToken).ConfigureAwait(false);
                return Usage;
        }
    }

    private static async Task<int> AuditAsync(
        IServiceProvider services, List<string> options, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (AuditOptions.Parse(options) is not { } parsed)
        {
            await Say(error, "rask audit takes --aggregate <Name> (any number of times), --unique-candidates and --json.\n" + Help, cancellationToken).ConfigureAwait(false);
            return Usage;
        }

        if (services.GetService<AmbientContextBinding>() is null)
        {
            await Say(error, "rask audit needs the app's database: this app has the Data battery off.", cancellationToken).ConfigureAwait(false);
            return Usage;
        }

        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            using var ambient = Db.UseScope(scope.ServiceProvider);
            RuleAuditReport report;
            try
            {
                report = await RuleAudit.RunAsync(parsed.Aggregates, cancellationToken, parsed.UniqueCandidates).ConfigureAwait(false);
            }
            catch (ArgumentException unknown)
            {
                await Say(error, unknown.Message, cancellationToken).ConfigureAwait(false);
                return Usage;
            }

            await Say(output, parsed.Json ? AuditOutput.Json(report) : AuditOutput.Text(report), cancellationToken).ConfigureAwait(false);
            return report.Clean ? Done : Found;
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider services, TextWriter error, CancellationToken cancellationToken)
    {
        if (services.GetService<IAppMigration>() is not { } migration)
        {
            await Say(error, "rask db migrate needs the app's database: this app has the Data battery off.", cancellationToken).ConfigureAwait(false);
            return Usage;
        }

        await migration.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return Done;
    }

    private static Task Say(TextWriter writer, string text, CancellationToken cancellationToken) =>
        writer.WriteLineAsync(text.AsMemory(), cancellationToken);
}
