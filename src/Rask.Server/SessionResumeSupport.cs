namespace Rask.Server;

/// <summary>
///     Whether this host can seal and open resume records, resolved once at startup.
/// </summary>
/// <remarks>
///     A holder rather than a nullable registration: a DI factory that returns <c>null</c> throws on
///     resolve, and inspecting the service collection for <c>IDataProtectionProvider</c> would make the
///     outcome depend on whether <c>AddRask</c> ran before or after the host registered it. This is always
///     resolvable and carries the answer inside, so the endpoint asks once and branches.
/// </remarks>
internal sealed class SessionResumeSupport
{
    internal SessionResumeSupport(SessionHandoffProtector? protector) => Protector = protector;

    /// <summary>The protector, or <c>null</c> when resume is off or the host has no Data Protection.</summary>
    internal SessionHandoffProtector? Protector { get; }

    internal bool Enabled => Protector is not null;
}
