using Microsoft.Extensions.Options;

namespace Rask.Background;

/// <summary>
/// Checks <see cref="JobsOptions"/> once <c>Rask:Jobs</c> and the callback have applied — at host start, so a
/// bad value fails fast, naming its key, rather than tearing down the host later.
/// </summary>
internal sealed class JobsOptionsValidator : IValidateOptions<JobsOptions>
{
    public ValidateOptionsResult Validate(string? name, JobsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckPolling(options, failures);
        CheckRetries(options, failures);
        CheckShutdownGracePeriod(options.ShutdownGracePeriod, failures);

        // `o.Run<Backup>();` with no cadence compiles and would then never run — the quietest possible
        // failure, so it is refused at boot instead.
        if (options.Recurring.FirstOrDefault(r => r.Schedule is null) is { } unscheduled)
        {
            failures.Add(
                $"Run<{unscheduled.Name}>() has no schedule. Finish it with .Every(1.Hour), .Daily.At(3, 0), "
                + ".Weekly.On(DayOfWeek.Monday).At(9, 0) or .Monthly.On(1).At(6, 0).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckPolling(JobsOptions options, List<string> failures)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Jobs:PollInterval must be positive.");
        }

        // Capped because the claim sends the candidate ids as an IN list. EF translates a parameterized
        // Contains to json_each / = ANY / OPENJSON rather than one parameter per id, so the classic
        // 999/2100 ceilings shouldn't bite — this is the belt to that pair of braces.
        if (options.BatchSize is < 1 or > JobsOptions.MaxBatchSize)
        {
            failures.Add($"Rask:Jobs:BatchSize must be between 1 and {JobsOptions.MaxBatchSize}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            failures.Add("Rask:Jobs:LeaseDuration must be positive.");
        }
        else if (options.LeaseDuration <= options.PollInterval)
        {
            // A lease that expires within one poll guarantees every job is stolen mid-flight by the next
            // instance to look — strictly worse than no lease at all, so it is refused rather than warned about.
            failures.Add(
                $"Rask:Jobs:LeaseDuration must be longer than PollInterval ({options.PollInterval}), "
                + "or every claimed job is stolen before it finishes.");
        }
    }

    private static void CheckRetries(JobsOptions options, List<string> failures)
    {
        if (options.MaxAttempts < 1)
        {
            failures.Add("Rask:Jobs:MaxAttempts must be at least 1.");
        }

        if (options.BaseRetryDelay < TimeSpan.Zero)
        {
            failures.Add("Rask:Jobs:BaseRetryDelay cannot be negative.");
        }

        if (options.MaxRetryDelay < options.BaseRetryDelay)
        {
            failures.Add("Rask:Jobs:MaxRetryDelay cannot be less than BaseRetryDelay.");
        }

        if (options.Retention < TimeSpan.Zero)
        {
            failures.Add("Rask:Jobs:Retention cannot be negative.");
        }
    }

    /// <summary>
    /// The upper bound is not pedantry: <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/> throws above
    /// <see cref="int.MaxValue"/> milliseconds, and it would throw from the shutdown path — the worst place to find
    /// out. Each battery carries its own copy; they are independent packages that must not reference each other.
    /// </summary>
    private static void CheckShutdownGracePeriod(TimeSpan value, List<string> failures)
    {
        if (value < TimeSpan.Zero)
        {
            failures.Add("Rask:Jobs:ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }
        else if (value.TotalMilliseconds > int.MaxValue)
        {
            failures.Add($"Rask:Jobs:ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }
}
