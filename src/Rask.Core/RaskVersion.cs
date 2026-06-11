using System.Reflection;

namespace Rask.Core;

/// <summary>
///     The running Rask framework version, taken from the assembly's informational version
///     (set by MinVer from the git tag). Use it to display or log which Rask build is in use.
/// </summary>
public static class RaskVersion
{
    /// <summary>
    ///     The Rask version string, e.g. <c>"0.7.0"</c> or a prerelease like
    ///     <c>"0.7.1-alpha.0.5"</c>. Build metadata (the <c>+&lt;sha&gt;</c> suffix) is stripped.
    /// </summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(RaskVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
