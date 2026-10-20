using System.Globalization;
using static Rask.Cli.Commands.DeployCommand;
using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The <c>docker</c> argument lists that touch the shared proxy's config, the deploy lock and the check through the
/// proxy, built without side effects. Everything runs in the proxy container or in a curl container sharing its
/// network namespace, so nothing has to be installed on the box itself.
/// </summary>
internal static class ProxyCommands
{
    /// <summary>The lock is a container: docker refuses a second one of the same name, which is what makes taking it atomic.</summary>
    internal const string LockContainer = "rask-deploy-lock";

    /// <summary>The label on the lock naming who holds it, so a waiting deploy can say what it waits for.</summary>
    internal const string LockOwnerLabel = "rask.lock";

    /// <summary>
    /// How long a lock lives. The container sleeps this long and removes itself, so a deploy that died holding the
    /// lock (a killed CI job, a dropped SSH connection) frees it on its own. The locked work takes well under a minute.
    /// </summary>
    internal const int LockLeaseSeconds = 600;

    /// <summary>The address Caddy's admin API answers on inside its container.</summary>
#pragma warning disable S1075, S5332 // Caddy's own admin endpoint: fixed by Caddy, plain HTTP, bound to the container's loopback only.
    internal const string AdminConfigUrl = "http://localhost:2019/config/";
#pragma warning restore S1075, S5332

    internal static IReadOnlyList<string> BuildNetworkExistsArguments(string host, string network) =>
        [.. Prefix(host), "network", "inspect", "--format", "{{.Name}}", network];

    /// <summary>Take the lock: a detached, self-removing container that sleeps for the lease.</summary>
    internal static IReadOnlyList<string> BuildLockArguments(string host, string owner) =>
    [
        .. Prefix(host), "run", "-d", "--rm", "--name", LockContainer, "--network", "none",
        "--label", $"{LockOwnerLabel}={owner}", "--entrypoint", "sleep", CaddyImage,
        LockLeaseSeconds.ToString(CultureInfo.InvariantCulture),
    ];

    internal static IReadOnlyList<string> BuildLockOwnerArguments(string host) =>
        [.. Prefix(host), "inspect", "--format", $"{{{{index .Config.Labels \"{LockOwnerLabel}\"}}}}", LockContainer];

    /// <summary>Release the lock by the id it was taken with, so a lease that ran out and was taken by another deploy is never removed from under it.</summary>
    internal static IReadOnlyList<string> BuildUnlockArguments(string host, string lockId) =>
        [.. Prefix(host), "rm", "-f", lockId];

    internal static IReadOnlyList<string> BuildProxyReadArguments(string host, string path) =>
        [.. Prefix(host), "exec", CaddyContainer, "cat", path];

    internal static IReadOnlyList<string> BuildSitesDirectoryArguments(string host) =>
        [.. Prefix(host), "exec", CaddyContainer, "mkdir", "-p", CaddySites.SitesDirectory];

    internal static IReadOnlyList<string> BuildSitesListArguments(string host) =>
        [.. Prefix(host), "exec", CaddyContainer, "ls", "-1", CaddySites.SitesDirectory];

    internal static IReadOnlyList<string> BuildProxyCopyArguments(string host, string localFile, string path) =>
        [.. Prefix(host), "cp", localFile, $"{CaddyContainer}:{path}"];

    /// <summary>A rename within one directory: atomic, so Caddy reads the old file or the new one and never half of one.</summary>
    internal static IReadOnlyList<string> BuildProxyMoveArguments(string host, string from, string to) =>
        [.. Prefix(host), "exec", CaddyContainer, "mv", "-f", from, to];

    internal static IReadOnlyList<string> BuildProxyRemoveFileArguments(string host, string path) =>
        [.. Prefix(host), "exec", CaddyContainer, "rm", "-f", path];

    /// <summary>The config Caddy is running, from its admin API — what it actually routes, not what a file says.</summary>
    internal static IReadOnlyList<string> BuildAdminConfigArguments(string host) =>
        [.. Prefix(host), "run", "--rm", "--network", $"container:{CaddyContainer}", CurlImage, "-fsS", "-m", "5", AdminConfigUrl];

    /// <summary>
    /// A request to <paramref name="site"/> through the proxy, as a visitor's arrives: HTTPS to the proxy itself with
    /// the site's name, so Caddy picks the site by its name. <c>-k</c> accepts a certificate a client could not check yet.
    /// Prints the status code.
    /// </summary>
    internal static IReadOnlyList<string> BuildThroughProxyArguments(string host, string site, string path) =>
    [
        .. Prefix(host), "run", "--rm", "--network", $"container:{CaddyContainer}", CurlImage,
        "-sk", "-m", "5", "-o", "/dev/null", "-w", "%{http_code}", "--resolve", $"{site}:443:127.0.0.1", $"https://{site}{path}",
    ];

    /// <summary>The upstream the proxy routes to, asked from the proxy's own network: the name has to resolve there and answer.</summary>
    internal static IReadOnlyList<string> BuildUpstreamFromProxyArguments(string host, string upstream, string path, bool failOnErrorStatus)
    {
        var args = new List<string>(Prefix(host)) { "run", "--rm", "--network", $"container:{CaddyContainer}", CurlImage };
        args.Add(failOnErrorStatus ? "-fsS" : "-sS");
#pragma warning disable S5332 // The proxy reaches the app over plain HTTP on the private docker network; TLS ends at the proxy.
        args.AddRange(["-m", "5", "-o", "/dev/null", $"http://{upstream}{path}"]);
#pragma warning restore S5332
        return args;
    }
}
