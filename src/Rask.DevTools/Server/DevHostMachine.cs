using System.Globalization;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Server;

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
