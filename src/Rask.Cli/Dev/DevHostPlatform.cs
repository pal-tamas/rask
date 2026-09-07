namespace Rask.Cli.Dev;

/// <summary>
///     The three machine-specific halves of the <c>.test</c> dev host: where a certificate authority is
///     trusted, how a privileged file gets written, and what it takes to answer on port 443.
/// </summary>
/// <remarks>
///     <para>
///         Everything above this class — deriving the hostname, minting the authority, issuing the
///         certificate, deciding what is missing — is identical everywhere and lives in
///         <see cref="DevHostSetup" />. Only these operations genuinely differ, and they differ a lot
///         more than they look like they should: the port is the clearest case, where Windows and Linux
///         let an ordinary process bind 443 and macOS does not, so two of the three platforms need no
///         redirect at all and the one that does needs a packet filter.
///     </para>
///     <para>
///         Each implementation is responsible for being <em>idempotent and cheap to ask</em>. A machine
///         that is already set up must produce an empty plan without spending a password, which is the
///         property that lets this run on every <c>rask dev</c> instead of being a separate command.
///     </para>
/// </remarks>
internal abstract class DevHostPlatform(IProcessRunner process, IConsole console)
{
    protected IProcessRunner Process { get; } = process;

    protected IConsole Console { get; } = console;

    /// <summary>The platform's hosts file.</summary>
    public abstract string HostsPath { get; }

    /// <summary>
    ///     The port Kestrel is told to listen on. 443 where an unprivileged process may bind it, and a
    ///     high port where something has to redirect.
    /// </summary>
    public abstract int HttpsPort { get; }

    /// <summary>How to describe trusting the authority, in the confirmation prompt.</summary>
    public abstract string TrustChange { get; }

    /// <summary>
    ///     How to describe this platform's port work, or null when it needs none. Null is the answer on
    ///     Windows, where Kestrel simply binds 443.
    /// </summary>
    public abstract string? PortChange { get; }

    /// <summary>Whether writing the hosts file needs elevation on this platform. It does everywhere so far.</summary>
    public virtual bool HostsNeedsPrivilege => true;

    /// <summary>
    ///     True when the authority with this fingerprint is already trusted. Compared by fingerprint
    ///     rather than by name so a stale authority from an earlier install — same subject, different
    ///     key — is replaced instead of mistaken for a match.
    /// </summary>
    public abstract Task<bool> IsTrustedAsync(string thumbprint, CancellationToken cancellationToken);

    /// <summary>Trusts the authority at <paramref name="certificatePath" />.</summary>
    public abstract Task<bool> TrustAsync(string certificatePath, CancellationToken cancellationToken);

    /// <summary>Replaces the hosts file with the staged copy at <paramref name="stagedPath" />.</summary>
    public abstract Task<bool> InstallHostsAsync(string stagedPath, CancellationToken cancellationToken);

    /// <summary>
    ///     A token describing the port work this machine still needs, or null when it needs none.
    ///     The token is opaque to the caller and only has to be stable, so an unchanged machine keeps
    ///     producing an empty plan.
    /// </summary>
    public abstract Task<string?> RequiredPortSetupAsync(CancellationToken cancellationToken);

    /// <summary>Carries out the port work described by <paramref name="token" />.</summary>
    public abstract Task<bool> ApplyPortSetupAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    ///     Asks for elevation once, up front, so the individual steps never stop to ask again mid-way
    ///     and leave the machine half configured.
    /// </summary>
    public abstract Task<bool> PrimePrivilegeAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Anything worth saying after a successful setup that the developer would otherwise discover as
    ///     a broken page — a browser this platform's trust store does not cover, most of all.
    /// </summary>
    public virtual Task WriteFollowUpAsync(CancellationToken cancellationToken)
    {
        // Firefox keeps its own NSS trust store and never consults the operating system's, so it is the
        // one browser still showing a warning after an otherwise successful setup. Said here for the
        // platforms that cannot do anything about it; Linux can, and overrides this.
        Console.WriteLine(
            "  Firefox keeps its own certificate store and won't trust this yet — see docs/cli.md.",
            ConsoleStyle.Dim);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The platform this is running on, or null where the dev host is not implemented. Null is not a
    ///     failure: <c>rask dev</c> simply behaves as it always has, on localhost.
    /// </summary>
    public static DevHostPlatform? Create(IProcessRunner process, IConsole console, DevHostStore store)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new MacDevHostPlatform(process, console, store);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsDevHostPlatform(process, console);
        }

        return OperatingSystem.IsLinux() ? new LinuxDevHostPlatform(process, console) : null;
    }

    protected Task<int> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        Process.RunAsync(file, arguments, workingDirectory: null, cancellationToken);

    protected Task<ProcessResult> CaptureAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        Process.CaptureAsync(file, arguments, workingDirectory: null, cancellationToken);
}
