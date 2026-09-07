using System.Globalization;
using System.Security.Cryptography;

namespace Rask.Cli.Dev;

/// <summary>The hostname the app is being served on, and what the child process needs to do it.</summary>
internal sealed record DevHostResult(string Hostname, string Url, IReadOnlyDictionary<string, string> Environment);

/// <summary>
///     Puts <c>rask dev</c> on <c>https://appname.test</c> instead of <c>http://localhost:5000</c>:
///     resolves the name, trusts a local authority once, issues a certificate for it, and redirects
///     ports 443 and 80 at the running app.
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
///         macOS only, for now. The shape is portable but the three mechanisms are not: the system
///         keychain, <c>/etc/hosts</c> and pf are all platform-specific, and Linux alone has several
///         competing trust stores. Elsewhere this is simply inert and <c>rask dev</c> behaves exactly as
///         it always has.
///     </para>
/// </remarks>
internal sealed class DevHostSetup(IConsole console, IProcessRunner processRunner, DevHostStore store)
{
    /// <summary>
    ///     Where Kestrel actually listens; pf maps port 443 onto it.
    /// </summary>
    /// <remarks>
    ///     The dev host is HTTPS only. No plaintext listener is bound at all, so there is nothing on
    ///     this machine that will serve the app unencrypted — which keeps the browser, the live
    ///     WebSocket (<c>wss://</c>, picked from the page's own scheme) and any cookie marked
    ///     <c>Secure</c> behaving in development exactly as they will in production.
    /// </remarks>
    public const int HttpsPort = 5001;

    private const string HostsPath = "/etc/hosts";
    private const string SystemKeychain = "/Library/Keychains/System.keychain";

    /// <summary>
    ///     Whether this platform is one the setup knows how to carry out. Everywhere else the feature is
    ///     absent rather than broken.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    ///     Prepares the machine and returns what the app needs to serve on its <c>.test</c> name, or
    ///     null to carry on with the ordinary localhost URLs.
    /// </summary>
    public async Task<DevHostResult?> TryPrepareAsync(
        string projectName,
        bool interactive,
        CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return null;
        }

        if (DevHostName.From(projectName) is not { } hostname)
        {
            return null;
        }

        try
        {
            var plan = await BuildPlanAsync(hostname, cancellationToken).ConfigureAwait(false);

            if (!plan.IsSatisfied && !await ApplyAsync(plan, interactive, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return Result(hostname);
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
    internal async Task<DevHostPlan> BuildPlanAsync(string hostname, CancellationToken cancellationToken)
    {
        var authority = store.ReadAuthority();
        var mintAuthority = authority is null || DevCertificates.NeedsReissue(authority, hostname: null, DateTimeOffset.Now);

        // A freshly minted authority is by definition not trusted yet; otherwise ask the keychain.
        var trustAuthority = mintAuthority
                             || !await IsAuthorityTrustedAsync(authority!.Value, cancellationToken).ConfigureAwait(false);

        // A certificate signed by an authority we are about to replace is worthless, whatever its dates.
        var issueCertificate = mintAuthority
                               || DevCertificates.NeedsReissue(store.ReadCertificate(hostname), hostname, DateTimeOffset.Now);

        var hosts = ReadHosts() is { } current ? DevHostFiles.AddHost(current, hostname) : null;

        var rules = DevHostFiles.PfRules(HttpsPort);
        var pfRules = rules is not null && !await IsPfLoadedAsync(rules, cancellationToken).ConfigureAwait(false)
            ? rules
            : null;

        return new DevHostPlan
        {
            Hostname = hostname,
            MintAuthority = mintAuthority,
            TrustAuthority = trustAuthority,
            IssueCertificate = issueCertificate,
            Hosts = hosts,
            PfRules = pfRules,
        };
    }

    /// <summary>Carries out <paramref name="plan" />. Returns false when the app should fall back to localhost.</summary>
    private async Task<bool> ApplyAsync(DevHostPlan plan, bool interactive, CancellationToken cancellationToken)
    {
        if (plan.NeedsPrivilege)
        {
            if (!interactive)
            {
                // A CI job, a piped run, an editor's run window with no terminal. Prompting would block
                // forever on something nobody can answer.
                console.WriteLine(
                    $"Serving on localhost — setting up https://{plan.Hostname} needs a password, and this run has no terminal to ask on.",
                    ConsoleStyle.Dim);
                return false;
            }

            if (!Confirm(plan))
            {
                console.WriteLine("Left the machine alone. Serving on localhost.", ConsoleStyle.Dim);
                return false;
            }

            // One prompt for the whole batch: prime sudo's credential cache up front so the individual
            // steps below never stop to ask again mid-way.
            if (await RunAsync("sudo", ["-v"], cancellationToken).ConfigureAwait(false) != 0)
            {
                console.WriteErrorLine("Couldn't get permission. Serving on localhost.", ConsoleStyle.Warning);
                return false;
            }
        }

        var authority = plan.MintAuthority
            ? Mint()
            : store.ReadAuthority()!.Value;

        if (plan.IssueCertificate)
        {
            store.WriteCertificate(
                plan.Hostname,
                DevCertificates.IssueServerCertificate(authority, plan.Hostname, DateTimeOffset.Now));
        }

        return await TrustAsync(plan, cancellationToken).ConfigureAwait(false)
               && await WriteHostsAsync(plan, cancellationToken).ConfigureAwait(false)
               && await LoadPfAsync(plan, cancellationToken).ConfigureAwait(false);

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
        console.WriteLine($"To serve this app on https://{plan.Hostname}, Rask needs your password once to:", ConsoleStyle.Heading);

        foreach (var change in plan.PrivilegedChanges)
        {
            console.WriteLine("  • " + change, ConsoleStyle.Dim);
        }

        console.Out.WriteLine();

        return new Prompt(console).Confirm("Set that up now?", @default: true);
    }

    private async Task<bool> TrustAsync(DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.TrustAuthority)
        {
            return true;
        }

        // -d installs into the admin (system) domain so every browser and every tool on the machine
        // agrees; a login-keychain trust would leave Safari and curl disagreeing about the same name.
        var exit = await RunAsync(
            "sudo",
            ["-n", "/usr/bin/security", "add-trusted-cert", "-d", "-r", "trustRoot", "-k", SystemKeychain, store.AuthorityCertificatePath],
            cancellationToken).ConfigureAwait(false);

        return exit == 0 || Fallback("trust the local certificate authority");
    }

    private async Task<bool> WriteHostsAsync(DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Hosts is null)
        {
            return true;
        }

        // Staged in a file we own and then installed, because there is no way to redirect a privileged
        // write through this process's own file handle. `install` sets the destination's mode and
        // ownership explicitly rather than inheriting whatever the staging file had.
        var staged = Path.Combine(Path.GetTempPath(), "rask-hosts-" + Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(staged, plan.Hosts, cancellationToken).ConfigureAwait(false);

            var exit = await RunAsync(
                "sudo",
                ["-n", "/usr/bin/install", "-m", "0644", "-o", "root", "-g", "wheel", staged, HostsPath],
                cancellationToken).ConfigureAwait(false);

            return exit == 0 || Fallback($"add {plan.Hostname} to {HostsPath}");
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

    private async Task<bool> LoadPfAsync(DevHostPlan plan, CancellationToken cancellationToken)
    {
        if (plan.PfRules is null)
        {
            return true;
        }

        var staged = Path.Combine(Path.GetTempPath(), "rask-pf-" + Guid.NewGuid().ToString("N"));

        try
        {
            await File.WriteAllTextAsync(staged, plan.PfRules, cancellationToken).ConfigureAwait(false);

            var loaded = await RunAsync(
                "sudo",
                ["-n", "/sbin/pfctl", "-a", DevHostFiles.PfAnchor, "-f", staged],
                cancellationToken).ConfigureAwait(false);

            if (loaded != 0)
            {
                return Fallback("redirect port 443");
            }

            // Rules in an anchor do nothing while pf itself is disabled, which is the macOS default.
            // Only enabled when it is actually off: `pfctl -E` bumps a reference count that is only
            // released by a matching -X, so enabling something already enabled slowly leaks tokens.
            if (!await IsPfEnabledAsync(cancellationToken).ConfigureAwait(false)
                && await RunAsync("sudo", ["-n", "/sbin/pfctl", "-E"], cancellationToken).ConfigureAwait(false) != 0)
            {
                return Fallback("enable the packet filter");
            }

            store.WritePfState(plan.PfRules, await BootIdAsync(cancellationToken).ConfigureAwait(false));
            return true;
        }
        finally
        {
            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
            }
        }
    }

    private bool Fallback(string what)
    {
        console.WriteErrorLine($"Couldn't {what}. Serving on localhost instead.", ConsoleStyle.Warning);
        return false;
    }

    /// <summary>What the child process needs in order to serve the name.</summary>
    private DevHostResult Result(string hostname) =>
        new(
            hostname,
            $"https://{hostname}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // HTTPS only, and bound to loopback: the name resolves to 127.0.0.1 and pf redirects
                // within lo0, so a listener on any wider address would only widen who can reach it.
                ["ASPNETCORE_URLS"] = $"https://127.0.0.1:{HttpsPort.ToString(CultureInfo.InvariantCulture)}",

                // Kestrel reads a PEM pair straight from configuration, so the certificate never has to
                // be installed anywhere or carry a password — the file mode is the whole boundary.
                ["ASPNETCORE_Kestrel__Certificates__Default__Path"] = store.CertificatePath(hostname),
                ["ASPNETCORE_Kestrel__Certificates__Default__KeyPath"] = store.KeyPath(hostname),
            });

    /// <summary>True when the system keychain already holds exactly this authority.</summary>
    private async Task<bool> IsAuthorityTrustedAsync(DevCertificate authority, CancellationToken cancellationToken)
    {
        string thumbprint;
        try
        {
            using var certificate = DevCertificates.Load(authority);
            thumbprint = certificate.Thumbprint;
        }
        catch (CryptographicException)
        {
            return false;
        }

        // Reading the system keychain needs no privilege, which is what lets the common case — already
        // set up, nothing to do — cost nothing at all.
        var found = await CaptureAsync(
            "/usr/bin/security",
            ["find-certificate", "-c", DevCertificates.AuthorityName, "-Z", SystemKeychain],
            cancellationToken).ConfigureAwait(false);

        // Compared by fingerprint, not by name: a stale authority from a previous install has the same
        // subject and would otherwise look like a match while signing nothing the browser accepts.
        return found.ExitCode == 0
               && found.StandardOutput.Contains(thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when these exact rules were loaded and the machine has not rebooted since.</summary>
    private async Task<bool> IsPfLoadedAsync(string rules, CancellationToken cancellationToken)
    {
        if (store.ReadPfState() is not { } state || !string.Equals(state.Rules, rules, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(state.BootId, await BootIdAsync(cancellationToken).ConfigureAwait(false), StringComparison.Ordinal);
    }

    private async Task<bool> IsPfEnabledAsync(CancellationToken cancellationToken)
    {
        var info = await CaptureAsync("sudo", ["-n", "/sbin/pfctl", "-s", "info"], cancellationToken).ConfigureAwait(false);
        return info.ExitCode == 0 && info.StandardOutput.Contains("Status: Enabled", StringComparison.Ordinal);
    }

    private async Task<string> BootIdAsync(CancellationToken cancellationToken)
    {
        var boot = await CaptureAsync("/usr/sbin/sysctl", ["-n", "kern.boottime"], cancellationToken).ConfigureAwait(false);
        return DevHostStore.BootId(boot.ExitCode == 0 ? boot.StandardOutput : null);
    }

    private Task<int> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processRunner.RunAsync(file, arguments, workingDirectory: null, cancellationToken);

    private Task<ProcessResult> CaptureAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processRunner.CaptureAsync(file, arguments, workingDirectory: null, cancellationToken);

    private static string? ReadHosts()
    {
        try
        {
            return File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : null;
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
