using Microsoft.Extensions.Options;
using Rask.SQLite;

namespace Rask.Logging;

/// <summary>
/// Checks <see cref="RaskLoggingOptions"/> once <c>Rask:Logging</c> and the callback have applied — at host start,
/// so a bad value fails fast, naming its key.
/// </summary>
internal sealed class RaskLoggingOptionsValidator : IValidateOptions<RaskLoggingOptions>
{
    public ValidateOptionsResult Validate(string? name, RaskLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckRetention(options, failures);
        CheckWriter(options, failures);
        CheckSqlite(options, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRetention(RaskLoggingOptions options, List<string> failures)
    {
        if (options.Retention < TimeSpan.Zero)
        {
            failures.Add("Rask:Logging:Retention cannot be negative.");
        }

        if (options.MaxRows < 0)
        {
            failures.Add("Rask:Logging:MaxRows cannot be negative.");
        }

        if (options.PurgeInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Logging:PurgeInterval must be positive.");
        }
    }

    private static void CheckWriter(RaskLoggingOptions options, List<string> failures)
    {
        if (options.FlushInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Logging:FlushInterval must be positive.");
        }

        if (options.BatchSize < 1)
        {
            failures.Add("Rask:Logging:BatchSize must be at least 1.");
        }

        if (options.MaxScopeValues < 1)
        {
            failures.Add("Rask:Logging:MaxScopeValues must be at least 1. Set CaptureScopes = false to store no scope state.");
        }

        if (options.MaxScopeValueLength < 1)
        {
            failures.Add("Rask:Logging:MaxScopeValueLength must be at least 1.");
        }

        if (options.QueueCapacity < 1)
        {
            failures.Add("Rask:Logging:QueueCapacity must be at least 1.");
        }

        if (options.ShutdownDrainTimeout < TimeSpan.Zero)
        {
            failures.Add("Rask:Logging:ShutdownDrainTimeout cannot be negative.");
        }
    }

    private static void CheckSqlite(RaskLoggingOptions options, List<string> failures)
    {
        if (options.BusyRetry is null)
        {
            failures.Add("Rask:Logging:BusyRetry is required.");
        }

        if (options.Pragmas is null)
        {
            failures.Add("Rask:Logging:Pragmas is required.");
            return;
        }

        // SqliteOptions.Validate() is internal to Rask.SQLite, but BuildScript throws on the same bad
        // values — so building the script here buys the identical fail-fast without reaching for internals.
        try
        {
            SqlitePragmas.BuildScript(options.Pragmas);
        }
        catch (ArgumentException ex)
        {
            failures.Add($"Rask:Logging:Pragmas: {ex.Message}");
        }
    }
}
