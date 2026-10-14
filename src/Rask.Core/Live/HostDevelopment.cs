using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     Whether the host running the current session is in Development, for what decides how much of an exception
///     a reader is shown: the session's own host first (<see cref="DevelopmentHost" />), and only without one the
///     process-wide <see cref="LiveOptions.IsDevelopment" /> a standalone host or a test sets.
/// </summary>
internal static class HostDevelopment
{
    /// <summary>The host's answer; <c>null</c> when neither the session's host nor the process has given one.</summary>
    internal static bool? Answer =>
        AmbientServices.Current?.GetService<DevelopmentHost>() is { IsDevelopment: { } host }
            ? host
            : LiveOptions.IsDevelopment;
}
