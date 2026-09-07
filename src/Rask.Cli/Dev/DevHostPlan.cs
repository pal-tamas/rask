namespace Rask.Cli.Dev;

/// <summary>
///     What the machine is missing before <c>https://appname.test</c> works, decided before anything is
///     changed and before any password is asked for.
/// </summary>
/// <remarks>
///     Separating the decision from the doing is what makes this feature honest. The plan is a pure
///     function of what is already on the machine, so it can be printed to the developer verbatim
///     before they are asked to approve it, asserted by tests without a Mac, and — most importantly —
///     come back empty. An empty plan is the normal case from the second run onwards, and it is the
///     reason <c>rask dev</c> can do this automatically without turning into a tool that asks for a
///     password every time you start your app.
/// </remarks>
internal sealed record DevHostPlan
{
    /// <summary>The hostname the app will be served on.</summary>
    public required string Hostname { get; init; }

    /// <summary>This machine has no local authority yet, or the stored one is unusable.</summary>
    public bool MintAuthority { get; init; }

    /// <summary>The authority is not in the system keychain (or a different one is).</summary>
    public bool TrustAuthority { get; init; }

    /// <summary>No usable certificate for <see cref="Hostname" />, or it is near expiry.</summary>
    public bool IssueCertificate { get; init; }

    /// <summary>The full new contents of the hosts file, or null when it already resolves the name.</summary>
    public string? Hosts { get; init; }

    /// <summary>
    ///     The platform's own description of the port work still needed, or null when none is — which is
    ///     always the answer on Windows, where an ordinary process may bind 443.
    /// </summary>
    public string? PortSetup { get; init; }

    /// <summary>
    ///     How this platform phrases its machine changes. Kept on the plan so the confirmation prompt can
    ///     be rendered — and asserted — without a platform to run on.
    /// </summary>
    public string? TrustChange { get; init; }

    /// <summary>How this platform phrases its port work.</summary>
    public string? PortChange { get; init; }

    /// <summary>The platform's hosts file, named in the prompt so the change is unambiguous.</summary>
    public string? HostsPath { get; init; }

    /// <summary>True when carrying it out needs the developer's password.</summary>
    public bool NeedsPrivilege => TrustAuthority || Hosts is not null || PortSetup is not null;

    /// <summary>True when the machine is already set up and there is nothing to do.</summary>
    public bool IsSatisfied =>
        !MintAuthority && !TrustAuthority && !IssueCertificate && Hosts is null && PortSetup is null;

    /// <summary>
    ///     The changes, phrased for the confirmation prompt. Only the privileged ones: minting and
    ///     issuing happen inside the developer's own home directory and are not what they are being
    ///     asked to approve.
    /// </summary>
    public IReadOnlyList<string> PrivilegedChanges
    {
        get
        {
            var changes = new List<string>();

            if (TrustAuthority && TrustChange is not null)
            {
                changes.Add(TrustChange);
            }

            if (Hosts is not null)
            {
                changes.Add($"add '127.0.0.1 {Hostname}' to {HostsPath ?? "the hosts file"}");
            }

            if (PortSetup is not null && PortChange is not null)
            {
                changes.Add(PortChange);
            }

            return changes;
        }
    }
}
