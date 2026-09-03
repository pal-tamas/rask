using Microsoft.Extensions.Hosting;

namespace Rask.Meta.Hosting;

/// <summary>
///     Where the built front end is, resolved once.
/// </summary>
/// <remarks>
///     Shared because two things need the same answer and must not disagree: the supervisor executes
///     the server entry under it, and the forwarder serves the client assets out of it. Resolving it
///     twice would be two chances to differ by a relative path.
/// </remarks>
internal sealed class MetaPaths
{
    // Public on an internal type, as elsewhere in this package: the container resolves only public
    // constructors, and an internal type's members are not public API.
    public MetaPaths(MetaHostingOptions options, IHostEnvironment environment)
    {
        AppDirectory = Path.IsPathRooted(options.AppDirectory)
            ? options.AppDirectory
            : Path.Combine(environment.ContentRootPath, options.AppDirectory);

        ServerEntry = Path.Combine(
            AppDirectory,
            options.Framework.ServerEntry.Replace('/', Path.DirectorySeparatorChar));

        WorkingDirectory = options.Framework.WorkingSubdirectory.Length == 0
            ? AppDirectory
            : Path.Combine(
                AppDirectory,
                options.Framework.WorkingSubdirectory.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The absolute path of the framework's build output directory.</summary>
    internal string AppDirectory { get; }

    /// <summary>The absolute path of the server entry the supervisor runs.</summary>
    internal string ServerEntry { get; }

    /// <summary>The directory the server entry is run from.</summary>
    internal string WorkingDirectory { get; }
}
