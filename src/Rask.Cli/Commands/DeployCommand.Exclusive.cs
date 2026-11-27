using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

internal sealed partial class DeployCommand
{
    /// <summary>What ends the line of an exclusive migration in <c>rask db pending</c>'s answer (Rask.Server's AppCommand).</summary>
    internal const string ExclusiveMark = " exclusive";

    /// <summary>
    ///     The new image's pending migrations that no older version of the app may run beside — one that rewrites what the
    ///     rows hold, such as Rask's time-ordered Guid rewrite on SQL Server. Empty when there is none, and when the image
    ///     cannot say (an app without Rask's <c>rask db pending</c>, a database it cannot reach): the deploy then runs as
    ///     it always did.
    /// </summary>
    private async Task<IReadOnlyList<string>> ExclusiveMigrationsAsync(
        string host, string slug, string? domain, IReadOnlyList<string> env, string tag, CancellationToken cancellationToken)
    {
        var runtimeEnv = WriteEnvFile(slug, env);
        try
        {
            var answer = await Capture(BuildPendingMigrationsArguments(host, slug, domain, env, tag, runtimeEnv), cancellationToken).ConfigureAwait(false);
            return answer.ExitCode == 0 ? ExclusiveIn(answer.StandardOutput) : [];
        }
        catch (TimeoutException)
        {
            // An image that started its server instead of answering: it knows no `rask db pending`.
            await Run(BuildRemoveArguments(host, PendingMigrationsContainer(slug)), cancellationToken).ConfigureAwait(false);
            return [];
        }
        finally
        {
            if (runtimeEnv is not null)
            {
                _fileSystem.TryDelete(runtimeEnv);
            }
        }
    }

    /// <summary>The exclusive migrations the new image would run, said as the reason the old version stops first.</summary>
    private async Task<IReadOnlyList<string>> StoppingForExclusiveAsync(
        string host, string slug, string? domain, IReadOnlyList<string> env, string tag, CancellationToken cancellationToken)
    {
        var exclusive = await ExclusiveMigrationsAsync(host, slug, domain, env, tag, cancellationToken).ConfigureAwait(false);
        if (exclusive.Count > 0)
        {
            SayStoppingForExclusive(slug, exclusive);
        }

        return exclusive;
    }

    /// <summary>
    ///     Domain mode: when the new image would run an exclusive migration, the old colour is stopped before the new one
    ///     starts, instead of after traffic moved to it. Nothing to ask on a first deploy, with no old colour.
    /// </summary>
    private async Task<IReadOnlyList<string>> StopForExclusiveAsync(
        string host, string slug, string domain, IReadOnlyList<string> env, string tag, string? current, string newContainer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(current))
        {
            return [];
        }

        var exclusive = await StoppingForExclusiveAsync(host, slug, domain, env, tag, cancellationToken).ConfigureAwait(false);
        if (exclusive.Count > 0)
        {
            await RetireOldColorsAsync(host, slug, newContainer, cancellationToken).ConfigureAwait(false);
        }

        return exclusive;
    }

    /// <summary>A start that failed after an exclusive migration: said, and not rolled back.</summary>
    private Task<int> StayDown(string slug, IReadOnlyList<string> exclusive, string? reason)
    {
        if (reason is not null)
        {
            Console.WriteErrorLine(reason, ConsoleStyle.Error);
        }

        SayNothingServesAfterExclusive(slug, exclusive);
        return Task.FromResult(1);
    }

    /// <summary>The ids of the lines of <c>rask db pending</c>'s answer that end in <see cref="ExclusiveMark" />.</summary>
    internal static IReadOnlyList<string> ExclusiveIn(string pending) =>
    [
        .. pending.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.EndsWith(ExclusiveMark, StringComparison.Ordinal))
            .Select(static line => line[..^ExclusiveMark.Length]),
    ];

    /// <summary>Why the old version is stopped before the new one starts, said before it is.</summary>
    private void SayStoppingForExclusive(string slug, IReadOnlyList<string> exclusive) =>
        Console.WriteErrorLine(
            $"  ! {string.Join(", ", exclusive)} rewrites what the rows hold, so the running {slug} would misread them once it ran: "
            + "it is stopped first and the new version started after it (stop-then-start, not side by side). The app is "
            + "down until the new version is healthy.",
            ConsoleStyle.Warning);

    /// <summary>What a failed start after an exclusive migration leaves: nothing to fall back on.</summary>
    private void SayNothingServesAfterExclusive(string slug, IReadOnlyList<string> exclusive) =>
        Console.WriteErrorLine(
            $"The previous version of {slug} was stopped for {string.Join(", ", exclusive)}, so nothing serves it now. It is not "
            + "brought back: if the migration ran, the old code would misread the rewritten rows. Fix the new version and deploy again.",
            ConsoleStyle.Error);
}
