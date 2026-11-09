using System.Reflection;

namespace Rask.Testing;

/// <summary>An app's build: the assembly <c>dotnet</c> runs, and the folder it runs in — its content root.</summary>
/// <param name="Host">The app's built assembly, <c>App.dll</c>.</param>
/// <param name="ContentRoot">Where its <c>appsettings.json</c> and <c>wwwroot</c> are: the app's project folder for a build, the
/// folder itself for a publish.</param>
internal sealed record AppBuild(string Host, string ContentRoot)
{
    // Written into the test assembly by build/Rask.Testing.Playwright.targets, one pair per referenced project.
    internal const string HostKey = "Rask.Testing.Playwright.Host:";
    internal const string ProjectKey = "Rask.Testing.Playwright.Project:";

    /// <summary>A built or published <c>App.dll</c>, run from its own folder.</summary>
    public static AppBuild At(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var full = Path.GetFullPath(host);
        return new AppBuild(full, Path.GetDirectoryName(full)!);
    }

    /// <summary>
    ///     The build of the app <paramref name="app" /> is: the app project's own output, run from the project's folder,
    ///     when the test project recorded it; otherwise the copy the build put beside the tests.
    /// </summary>
    public static AppBuild Of(Assembly app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var name = app.GetName().Name;
        string? host = null;
        string? project = null;
        foreach (var metadata in Recorded())
        {
            if (string.Equals(metadata.Key, HostKey + name, StringComparison.Ordinal))
            {
                host ??= metadata.Value;
            }
            else if (string.Equals(metadata.Key, ProjectKey + name, StringComparison.Ordinal))
            {
                project ??= metadata.Value;
            }
        }

        if (host is not null && project is not null && File.Exists(host))
        {
            return new AppBuild(host, Path.GetDirectoryName(project)!);
        }

        return At(app.Location);
    }

    // The test assembly records them; under a runner that is not the entry assembly, every loaded one is asked.
    private static IEnumerable<AssemblyMetadataAttribute> Recorded()
    {
        var entry = Assembly.GetEntryAssembly();
        var assemblies = entry is null
            ? AppDomain.CurrentDomain.GetAssemblies()
            : [entry, .. AppDomain.CurrentDomain.GetAssemblies().Where(a => a != entry)];
        return assemblies
            .Where(a => !a.IsDynamic)
            .SelectMany(a => a.GetCustomAttributes<AssemblyMetadataAttribute>())
            .Where(m => m.Key.StartsWith(HostKey, StringComparison.Ordinal) || m.Key.StartsWith(ProjectKey, StringComparison.Ordinal));
    }
}
