using System.Globalization;
using System.Reflection;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Server;

/// <summary>
///     The <c>https://&lt;name&gt;.test</c> address an editor-launched app is served on, when <c>rask dev</c>
///     has already set that name up on this machine.
/// </summary>
/// <remarks>
///     <para>
///         F5 has no terminal and no chance to ask for a password, so it never sets anything up: it uses the
///         name if everything <c>rask dev</c> would have made is already there, and otherwise leaves the app
///         on the localhost addresses its launch profile names. "Already there" is read from the machine
///         every time, the same way <c>rask dev</c> decides — a certificate and key for the name, and the
///         name inside the hosts-file block <c>rask dev</c> owns.
///     </para>
///     <para>
///         Skipped when the project has islands, exactly as <c>rask dev</c> skips it: islands load their
///         modules from a Vite dev server over plain HTTP, which an HTTPS page is not allowed to do at all.
///     </para>
/// </remarks>
/// <param name="Hostname">The <c>.test</c> name.</param>
/// <param name="Port">The port Kestrel listens on for it.</param>
/// <param name="CertificatePath">The PEM certificate <c>rask dev</c> issued for the name.</param>
/// <param name="KeyPath">Its PEM private key.</param>
internal sealed record EditorDevHost(string Hostname, int Port, string CertificatePath, string KeyPath)
{
    /// <summary>
    ///     The address to open. The port is named whenever it is not 443: on macOS the pf redirect that
    ///     carries 443 to it resets on reboot and cannot be checked without root, and the explicit port works
    ///     either way.
    /// </summary>
    internal string Url => Port == DevHostPaths.DirectHttpsPort
        ? "https://" + Hostname
        : string.Create(CultureInfo.InvariantCulture, $"https://{Hostname}:{Port}");

    /// <summary>
    ///     The dev host to serve on, or null to leave the app on its launch profile's addresses.
    /// </summary>
    /// <param name="entryAssembly">The app assembly, which says whether this build was a dev session.</param>
    /// <param name="isDevelopment">The host environment's answer.</param>
    /// <param name="applicationName">The app's name — the project name <c>rask dev</c> derives the host from.</param>
    /// <param name="contentRoot">The app's content root, where an islands project has its dev-server pointer.</param>
    /// <param name="readEnv">Reads an environment variable.</param>
    /// <param name="machine">What is on this machine; a seam so tests never read the real one.</param>
    internal static EditorDevHost? Resolve(
        Assembly? entryAssembly,
        bool isDevelopment,
        string? applicationName,
        string contentRoot,
        Func<string, string?> readEnv,
        IDevHostMachine machine)
    {
        ArgumentNullException.ThrowIfNull(readEnv);
        ArgumentNullException.ThrowIfNull(machine);

        if (!EditorDevSession.IsActive(entryAssembly, isDevelopment, readEnv))
        {
            return null;
        }

        if (machine.FileExists(IslandDevPointer.PathFor(contentRoot)))
        {
            return null;
        }

        if (DevHostName.From(applicationName) is not { } hostname)
        {
            return null;
        }

        var certificate = DevHostPaths.CertificatePath(machine.Root, hostname);
        var key = DevHostPaths.KeyPath(machine.Root, hostname);
        if (!machine.FileExists(certificate) || !machine.FileExists(key))
        {
            return null;
        }

        if (machine.ReadHosts() is not { } hosts
            || !DevHostFiles.ManagedHosts(hosts).Contains(hostname, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var port = machine.HttpsPort;

        // Linux lets an ordinary process bind 443 only after the one sysctl `rask dev` sets, and that resets
        // on reboot. Binding a port the kernel refuses would fail the app's startup, so check first.
        if (port < 1024 && machine.UnprivilegedPortStart is { } start && start > port)
        {
            return null;
        }

        return new EditorDevHost(hostname, port, certificate, key);
    }
}

/// <summary>The machine facts <see cref="EditorDevHost.Resolve" /> reads.</summary>
internal interface IDevHostMachine
{
    /// <summary>Where <c>rask dev</c> keeps its state: <c>~/.rask</c>.</summary>
    string Root { get; }

    /// <summary>The port the dev host listens on here.</summary>
    int HttpsPort { get; }

    /// <summary>Linux's <c>ip_unprivileged_port_start</c>, or null where there is no such limit to read.</summary>
    int? UnprivilegedPortStart { get; }

    bool FileExists(string path);

    /// <summary>The hosts file's text, or null when it cannot be read.</summary>
    string? ReadHosts();
}

/// <summary>The real machine.</summary>
internal sealed class DevHostMachine : IDevHostMachine
{
    private const string UnprivilegedPortStartPath = "/proc/sys/net/ipv4/ip_unprivileged_port_start";

    public string Root => DevHostPaths.DefaultRoot;

    public int HttpsPort => DevHostPaths.HttpsPort;

    public int? UnprivilegedPortStart
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return null;
            }

            try
            {
                // Readable without privilege; unreadable means an unusual kernel, so assume the default.
                return int.TryParse(File.ReadAllText(UnprivilegedPortStartPath).Trim(), CultureInfo.InvariantCulture, out var start)
                    ? start
                    : 1024;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 1024;
            }
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public string? ReadHosts()
    {
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts")
            : "/etc/hosts";

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
