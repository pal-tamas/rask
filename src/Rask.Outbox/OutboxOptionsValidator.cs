using Microsoft.Extensions.Options;

namespace Rask.Outbox;

/// <summary>
/// Checks <see cref="OutboxOptions"/> once <c>Rask:Outbox</c> and the callback have applied — at host start, so a
/// bad value fails fast there rather than throwing out of <c>new PeriodicTimer(...)</c> on the background thread,
/// which, with the default <c>BackgroundServiceExceptionBehavior.StopHost</c>, takes the host down at an unrelated
/// moment with an unrelated-looking stack.
/// </summary>
internal sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckPolling(options, failures);
        CheckRetries(options, failures);
        CheckShutdownGracePeriod(options.ShutdownGracePeriod, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckPolling(OutboxOptions options, List<string> failures)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Outbox:PollInterval must be positive.");
        }

        // Capped because the claim sends the candidate ids as an IN list. EF translates a parameterized
        // Contains to json_each / = ANY / OPENJSON rather than one parameter per id, so the classic
        // 999/2100 ceilings shouldn't bite — this is the belt to that pair of braces.
        if (options.BatchSize is < 1 or > OutboxOptions.MaxBatchSize)
        {
            failures.Add($"Rask:Outbox:BatchSize must be between 1 and {OutboxOptions.MaxBatchSize}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            failures.Add("Rask:Outbox:LeaseDuration must be positive.");
        }
        else if (options.LeaseDuration <= options.PollInterval)
        {
            // A lease that expires within one poll guarantees every message is stolen mid-flight by the next
            // instance to look — strictly worse than no lease, so it is refused rather than warned about.
            failures.Add(
                $"Rask:Outbox:LeaseDuration must be longer than PollInterval ({options.PollInterval}), "
                + "or every claimed message is stolen before it finishes.");
        }
    }

    private static void CheckRetries(OutboxOptions options, List<string> failures)
    {
        if (options.MaxAttempts < 1)
        {
            failures.Add("Rask:Outbox:MaxAttempts must be at least 1.");
        }

        if (options.RetentionPeriod < TimeSpan.Zero)
        {
            failures.Add("Rask:Outbox:RetentionPeriod cannot be negative.");
        }
    }

    // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds, and it would throw
    // from the shutdown path — the worst place to find out.
    private static void CheckShutdownGracePeriod(TimeSpan value, List<string> failures)
    {
        if (value < TimeSpan.Zero)
        {
            failures.Add("Rask:Outbox:ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }
        else if (value.TotalMilliseconds > int.MaxValue)
        {
            failures.Add($"Rask:Outbox:ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }
}
