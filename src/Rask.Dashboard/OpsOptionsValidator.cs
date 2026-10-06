using Microsoft.Extensions.Options;

namespace Rask.Dashboard;

/// <summary>
/// Checks <see cref="OpsOptions"/> once <c>Rask:Ops</c> and the callback have applied — at host
/// start, so a bad value fails fast, naming its key.
/// </summary>
internal sealed class OpsOptionsValidator : IValidateOptions<OpsOptions>
{
    public ValidateOptionsResult Validate(string? name, OpsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.RefreshInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Ops:RefreshInterval must be positive.");
        }

        if (options.MaxPollDuration < TimeSpan.Zero)
        {
            failures.Add("Rask:Ops:MaxPollDuration cannot be negative.");
        }

        if (options.PageSize < 1)
        {
            failures.Add("Rask:Ops:PageSize must be at least 1.");
        }

        if (options.LogBufferSize < 1)
        {
            failures.Add("Rask:Ops:LogBufferSize must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
