using System.Globalization;
using System.Security.Cryptography;
using Rask.Hosting.Shared;

namespace Rask.Cli.Dev;

/// <summary>The hostname the app is being served on, and what the child process needs to do it.</summary>
internal sealed record DevHostResult(string Hostname, string Url, IReadOnlyDictionary<string, string> Environment);

/// <summary>
///     Puts <c>rask dev</c> on <c>https://appname.test</c> instead of <c>http://localhost:5000</c>:
///     resolves the name, trusts a local authority once, issues a certificate for it, and makes sure
///     the app can answer on port 443.
/// </summary>
/// <remarks>
///     <para>
///         Three rules govern everything here, in order. <b>It never fails the dev loop</b> — every
///         failure path falls back to plain localhost with a note, because a developer who wanted to run
///         their app must always end up running their app. <b>It asks once</b> — the plan is recomputed
///         from the machine each run and comes back empty afterwards, so the password prompt belongs to
///         first-time setup rather than to starting an app. <b>It never surprises</b> — nothing
///         privileged happens without showing exactly what will change and waiting for a yes.
///     </para>
///     <para>
///         Everything in this class is the same on every platform. The parts that genuinely differ —
///         where an authority is trusted, how a privileged file is written, and what it takes to answer
///         on 443 — live behind <see cref="DevHostPlatform" />.
///     </para>
/// </remarks>
internal sealed class DevHostSetup(IConsole console, IProcessRunner processRunner, DevHostStore store)
{
    /// <summary>
    ///     Whether the dev host is implemented for this platform. Everywhere else it is absent rather
    ///     than broken, and <c>rask dev</c> behaves exactly as it always has.
    /// </summary>
    public static bool IsSupported =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>
    ///     Prepares the machine and returns what the app needs to serve on its <c>.test</c> name, or
    ///     null to carry on with the ordinary localhost URLs.
    /// </summary>
    public async Task<DevHostResult?> TryPrepareAsync(
        string projectName,
        bool interactive,
        CancellationToken cancellationToken)
    {
        if (DevHostPlatform.Create(processRunner, console, store) is not { } platform)
        {
            return null;
        }

        if (DevHostName.From(projectName) is not { } hostname)
        {
            return null;
        }

        try
        {
            var plan = await BuildPlanAsync(platform, hostname, cancellationToken).ConfigureAwait(false);

            if (!plan.IsSatisfied)
            {
                if (!await ApplyAsync(platform, plan, interactive, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                if (plan.TrustAuthority)
                {
                    await platform.WriteFollowUpAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return Result(platform, hostname);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // The whole feature is a convenience over running the app. Anything unexpected here means
            // the developer gets the localhost URL they would have had anyway, plus an explanation —
            // never a dev server that refused to start over a certificate.
            console.WriteErrorLine($"Couldn't set up https://{hostname} ({exception.Message}). Using localhost instead.", ConsoleStyle.Warning);
            return null;
        }
    }

    /// <summary>
    ///     Works out what is missing. Reads only — nothing here changes the machine, and nothing here
    ///     needs a password.
    /// </summary>
    internal async Task<DevHostPlan> BuildPlanAsync(
        DevHostPlatform platform,
        string hostname,
        CancellationToken cancellationToken)
    {
        var authority = store.ReadAuthority();
        var mintAuthority = authority is null || DevCertificates.NeedsReissue(authority, hostname: null, DateTimeOffset.Now);

        // A freshly minted authority is by definition not trusted yet; otherwise ask the platform.
        var trustAuthority = mintAuthority || !await IsTrustedAsync(platform, authority!.Value, cancellationToken).ConfigureAwait(false);

        // A certificate signed by an authority we are about to replace is worthless, whatever its dates.
        var issueCertificate = mintAuthority
                               || DevCertificates.NeedsReissue(store.ReadCertificate(hostname), hostname, DateTimeOffset.Now);

        var hosts = ReadHosts(platform) is { } current ? DevHostFiles.AddHost(current, hostname) : null;

        return new DevHostPlan
        {
            Hostname = hostname,
            MintAuthority = mintAuthority,
            TrustAuthority = trustAuthority,
            IssueCertificate = issueCertificate,
            Hosts = hosts,
            PortSetup = await platform.RequiredPortSetupAsync(cancellationToken).ConfigureAwait(false),
            TrustChange = platform.TrustChange,
            PortChange = platform.PortChange,
            HostsPath = platform.HostsPath,
        };
    }

    /// <summary>Carries out <paramref name="plan" />. Returns false when the app should fall back to localhost.</summary>
    private async Task<bool> ApplyAsync(
        DevHostPlatform platform,
        DevHostPlan plan,
        bool interactive,
        CancellationToken cancellationToken)
    {
        if (plan.NeedsPrivilege)
        {
            if (!interactive)
            {
                // A CI job, a piped run, an editor's run window with no terminal. Prompting would block
                // forever on something nobody can answer.
                console.WriteLine(
                    $"Serving on localhost — setting up https://{plan.Hostname} needs permission, and this run has no terminal to ask on.",
                    ConsoleStyle.Dim);
                return false;
            }

            if (!Confirm(plan))
            {
                console.WriteLine("Left the machine alone. Serving on localhost.", ConsoleStyle.Dim);
                return false;
            }

            // One prompt for the whole batch where the platform has such a thing, so the individual steps
            // below never stop to ask again mid-way and leave the machine half configured.
            if (!await platform.PrimePrivilegeAsync(cancellationToken).ConfigureAwait(false))
            {
                console.WriteErrorLine("Couldn't get permission. Serving on localhost.", ConsoleStyle.Warning);
                return false;
            }
        }

        var authority = plan.MintAuthority ? Mint() : store.ReadAuthority()!.Value;

        if (plan.IssueCertificate)
        {
            store.WriteCertificate(
                plan.Hostname,
                DevCertificates.IssueServerCertificate(authority, plan.Hostname, DateTimeOffset.Now));
        }

        return await TrustAsync(platform, plan, cancellationToken).ConfigureAwait(false)
               && await WriteHostsAsync(platform, plan, cancellationToken).ConfigureAwait(false)
               && await ApplyPortAsync(platform, plan, cancellationToken).ConfigureAwait(false);

        DevCertificate Mint()
        {
            var minted = DevCertificates.CreateAuthority(DateTimeOffset.Now);
            store.WriteAuthority(minted);
            return minted;
        }
    }

    private bool Confirm(DevHostPlan plan)
    {
        console.Out.WriteLine();
        console.WriteLine($"To serve this app on https://{plan.Hostname}, Rask needs permission once to:", ConsoleStyle.Heading);

        foreach (var change in plan.PrivilegedChanges)
        {
            console.WriteLine("  • " + change, ConsoleStyle.Dim);
        }

        console.Out.WriteLine();

        return new Prompt(console).Confirm("Set that up now?", @default: true);
    }

    private async Task<bool> TrustAsync(DevHostPlatform platform, DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.TrustAuthority)
        {
            return true;
        }

        return await platform.TrustAsync(store.AuthorityCertificatePath, cancellationToken).ConfigureAwait(false)
               || Fallback("trust the local certificate authority");
    }

    private async Task<bool> WriteHostsAsync(DevHostPlatform platform, DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Hosts is null)
        {
            return true;
        }

        // Staged in a file we own and then installed, because there is no way to redirect a privileged
        // write through this process's own file handle.
        var staged = Path.Combine(Path.GetTempPath(), "rask-hosts-" + Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(staged, plan.Hosts, cancellationToken).ConfigureAwait(false);

            return await platform.InstallHostsAsync(staged, cancellationToken).ConfigureAwait(false)
                   || Fallback($"add {plan.Hostname} to {platform.HostsPath}");
        }
        finally
        {
            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
                // Tidying up a temp file is not worth failing over.
            }
        }
    }

    private async Task<bool> ApplyPortAsync(DevHostPlatform platform, DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (plan.PortSetup is null)
        {
            return true;
        }

        return await platform.ApplyPortSetupAsync(plan.PortSetup, cancellationToken).ConfigureAwait(false)
               || Fallback($"let this app answer on port {platform.HttpsPort.ToString(CultureInfo.InvariantCulture)}");
    }

    private bool Fallback(string what)
    {
        console.WriteErrorLine($"Couldn't {what}. Serving on localhost instead.", ConsoleStyle.Warning);
        return false;
    }

    /// <summary>What the child process needs in order to serve the name.</summary>
    private DevHostResult Result(DevHostPlatform platform, string hostname) =>
        new(
            hostname,
            $"https://{hostname}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // HTTPS only, and bound to loopback: the name resolves to 127.0.0.1, so a listener on
                // any wider address would only widen who can reach it.
                ["ASPNETCORE_URLS"] = $"https://127.0.0.1:{platform.HttpsPort.ToString(CultureInfo.InvariantCulture)}",

                // Kestrel reads a PEM pair straight from configuration, so the certificate never has to
                // be installed anywhere or carry a password — the file mode is the whole boundary.
                ["ASPNETCORE_Kestrel__Certificates__Default__Path"] = store.CertificatePath(hostname),
                ["ASPNETCORE_Kestrel__Certificates__Default__KeyPath"] = store.KeyPath(hostname),
            });

    private static async Task<bool> IsTrustedAsync(
        DevHostPlatform platform,
        DevCertificate authority,
        CancellationToken cancellationToken)
    {
        try
        {
            using var certificate = DevCertificates.Load(authority);
            return await platform.IsTrustedAsync(certificate.Thumbprint, cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string? ReadHosts(DevHostPlatform platform)
    {
        try
        {
            return File.Exists(platform.HostsPath) ? File.ReadAllText(platform.HostsPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
