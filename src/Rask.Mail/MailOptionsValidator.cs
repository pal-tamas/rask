using Microsoft.Extensions.Options;

namespace Rask.Mailing;

/// <summary>
/// Checks <see cref="MailOptions"/> once <c>Rask:Mail</c> and the callback have applied — at host start, so a
/// bad value fails fast, naming its key, rather than tearing down the host later.
/// </summary>
internal sealed class MailOptionsValidator : IValidateOptions<MailOptions>
{
    public ValidateOptionsResult Validate(string? name, MailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckSender(options, failures);
        CheckPolling(options, failures);
        CheckRetries(options, failures);
        CheckShutdownGracePeriod(options.ShutdownGracePeriod, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckSender(MailOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.From))
        {
            failures.Add("Rask:Mail:From is required — set a default sender address.");
        }

        if (options.Smtp is not null && string.IsNullOrWhiteSpace(options.Smtp.Host))
        {
            failures.Add("Rask:Mail:Smtp:Host is required when SMTP is configured.");
        }
    }

    private static void CheckPolling(MailOptions options, List<string> failures)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Mail:PollInterval must be positive.");
        }

        // Capped because the claim sends the candidate ids as an IN list. EF translates a parameterized
        // Contains to json_each / = ANY / OPENJSON rather than one parameter per id, so the classic
        // 999/2100 ceilings shouldn't bite — this is the belt to that pair of braces.
        if (options.BatchSize is < 1 or > MailOptions.MaxBatchSize)
        {
            failures.Add($"Rask:Mail:BatchSize must be between 1 and {MailOptions.MaxBatchSize}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            failures.Add("Rask:Mail:LeaseDuration must be positive.");
        }
        else if (options.LeaseDuration <= options.PollInterval)
        {
            // A lease that expires within one poll guarantees every email is stolen mid-flight by the next
            // instance to look — and a stolen send is a second copy in someone's inbox.
            failures.Add(
                $"Rask:Mail:LeaseDuration must be longer than PollInterval ({options.PollInterval}), "
                + "or every claimed email is stolen before it finishes.");
        }
    }

    private static void CheckRetries(MailOptions options, List<string> failures)
    {
        if (options.MaxAttempts < 1)
        {
            failures.Add("Rask:Mail:MaxAttempts must be at least 1.");
        }

        if (options.BaseRetryDelay < TimeSpan.Zero)
        {
            failures.Add("Rask:Mail:BaseRetryDelay cannot be negative.");
        }

        if (options.MaxRetryDelay < options.BaseRetryDelay)
        {
            failures.Add("Rask:Mail:MaxRetryDelay cannot be less than BaseRetryDelay.");
        }

        if (options.RetentionPeriod < TimeSpan.Zero)
        {
            failures.Add("Rask:Mail:RetentionPeriod cannot be negative.");
        }
    }

    // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds, and it would throw
    // from the shutdown path — the worst place to find out.
    private static void CheckShutdownGracePeriod(TimeSpan value, List<string> failures)
    {
        if (value < TimeSpan.Zero)
        {
            failures.Add("Rask:Mail:ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }
        else if (value.TotalMilliseconds > int.MaxValue)
        {
            failures.Add($"Rask:Mail:ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }
}
