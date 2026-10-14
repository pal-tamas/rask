namespace Rask.Core.Live;

/// <summary>
///     One host's own answer to whether it runs in Development, registered in that host's services so a session
///     reads its host's answer and not the process-wide <see cref="LiveOptions.IsDevelopment" />.
/// </summary>
/// <remarks>
///     That flag is claimed by the first host to map in the process and never revised, so in a process holding two
///     hosts (a test run, a dev tool beside the app) a Production host could take a Development host's answer and
///     show its readers an exception's message. <c>null</c> when the host could not say: the flag then answers.
/// </remarks>
internal sealed class DevelopmentHost(bool? isDevelopment)
{
    /// <summary>Whether the host is in Development, or <c>null</c> when it could not say.</summary>
    internal bool? IsDevelopment { get; } = isDevelopment;
}
