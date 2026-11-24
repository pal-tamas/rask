using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Runs what waits for a <c>Db.Transaction</c>'s commit, once it has committed: every step, in order — a step that
///     throws no longer skips the ones behind it — and a failure met as <see cref="RaskDataOptions.AfterCommitFailures" />
///     says.
/// </summary>
internal static partial class AfterCommitSteps
{
    public static async Task Run(IReadOnlyList<Func<Task>> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        List<Exception>? failures = null;
        foreach (var step in steps)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The work is committed: one step's failure must not skip the steps behind it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is null)
        {
            return;
        }

        var services = Db.ScopeServices ?? Ambient.Services;
        var policy = services?.GetService<IOptions<RaskDataOptions>>()?.Value.AfterCommitFailures ?? AfterCommitFailure.Log;
        if (policy == AfterCommitFailure.Throw)
        {
            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Throw(failures[0]);
            }

            throw new AggregateException(failures);
        }

        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(AfterCommitSteps).FullName!) ?? NullLogger.Instance;
        foreach (var failure in failures)
        {
            StepFailed(logger, failure);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A step after a committed Db.Transaction failed; the work stays committed and the steps after it ran. "
            + "Set RaskDataOptions.AfterCommitFailures = Throw to hand the failure to the caller instead.")]
    private static partial void StepFailed(ILogger logger, Exception exception);
}
