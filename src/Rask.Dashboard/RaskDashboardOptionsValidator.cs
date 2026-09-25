using Microsoft.Extensions.Options;

namespace Rask.Dashboard;

/// <summary>
/// Checks <see cref="RaskDashboardOptions"/> once <c>Rask:Dashboard</c> and the callback have applied — at host
/// start, so a bad value fails fast, naming its key.
/// </summary>
internal sealed class RaskDashboardOptionsValidator : IValidateOptions<RaskDashboardOptions>
{
    public ValidateOptionsResult Validate(string? name, RaskDashboardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.RefreshInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Dashboard:RefreshInterval must be positive.");
        }

        if (options.MaxPollDuration < TimeSpan.Zero)
        {
            failures.Add("Rask:Dashboard:MaxPollDuration cannot be negative.");
        }

        if (options.PageSize < 1)
        {
            failures.Add("Rask:Dashboard:PageSize must be at least 1.");
        }

        if (options.LogBufferSize < 1)
        {
            failures.Add("Rask:Dashboard:LogBufferSize must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
