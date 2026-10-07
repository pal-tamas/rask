using Microsoft.Extensions.Options;
using Rask.SQLite;

namespace Rask.Logging;

/// <summary>
/// Checks <see cref="LogsOptions"/> once <c>Rask:Logs</c> and the callback have applied — at host start,
/// so a bad value fails fast, naming its key.
/// </summary>
internal sealed class LogsOptionsValidator : IValidateOptions<LogsOptions>
{
    public ValidateOptionsResult Validate(string? name, LogsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        CheckRetention(options, failures);
        CheckWriter(options, failures);
        CheckSqlite(options, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckRetention(LogsOptions options, List<string> failures)
    {
        if (options.Retention < TimeSpan.Zero)
        {
            failures.Add("Rask:Logs:Retention cannot be negative.");
        }

        if (options.MaxRows < 0)
        {
            failures.Add("Rask:Logs:MaxRows cannot be negative.");
        }

        if (options.SweepInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Logs:SweepInterval must be positive.");
        }
    }

    private static void CheckWriter(LogsOptions options, List<string> failures)
    {
        if (options.FlushInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Logs:FlushInterval must be positive.");
        }

        if (options.BatchSize < 1)
        {
            failures.Add("Rask:Logs:BatchSize must be at least 1.");
        }

        if (options.MaxScopeValues < 1)
        {
            failures.Add("Rask:Logs:MaxScopeValues must be at least 1. Set CaptureScopes = false to store no scope state.");
        }

        if (options.MaxScopeValueLength < 1)
        {
            failures.Add("Rask:Logs:MaxScopeValueLength must be at least 1.");
        }

        if (options.QueueCapacity < 1)
        {
            failures.Add("Rask:Logs:QueueCapacity must be at least 1.");
        }

        if (options.ShutdownGracePeriod < TimeSpan.Zero)
        {
            failures.Add("Rask:Logs:ShutdownGracePeriod cannot be negative.");
        }
    }

    private static void CheckSqlite(LogsOptions options, List<string> failures)
    {
        if (options.BusyRetry is null)
        {
            failures.Add("Rask:Logs:BusyRetry is required.");
        }

        if (options.Pragmas is null)
        {
            failures.Add("Rask:Logs:Pragmas is required.");
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
            failures.Add($"Rask:Logs:Pragmas: {ex.Message}");
        }
    }
}
