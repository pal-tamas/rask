using System.Text.Json;

namespace Rask.Hosting.Shared;

/// <summary>
///     The pointer an islands build drops when it is serving from a dev server: where Vite will listen, and
///     the config it runs.
/// </summary>
/// <remarks>
///     <para>
///         Written by <c>Rask.External.targets</c> to the STABLE <c>obj/rask-external/</c> path, not under
///         <c>obj/Debug/net10.0/</c>, because neither reader knows which configuration or target framework the
///         build chose. It carries the config's real path, so no reader has to know how Rask lays out obj/.
///     </para>
///     <para>
///         Two readers start the same server from it: <c>rask dev</c>, beside the app, and an app an editor
///         launched under its debugger, where there is no <c>rask dev</c> to do it.
///     </para>
/// </remarks>
/// <param name="Url">Where the island dev server listens — the origin the page imports modules from.</param>
/// <param name="Config">The generated Vite config to run.</param>
internal readonly record struct IslandDevPointer(string Url, string Config)
{
    /// <summary>The pointer file for the project at <paramref name="projectDirectory" />.</summary>
    internal static string PathFor(string projectDirectory) =>
        Path.Combine(projectDirectory, "obj", "rask-external", "dev.json");

    /// <summary>The pointer, or null when there is none yet, or it names a config that does not exist.</summary>
    internal static IslandDevPointer? TryRead(string projectDirectory)
    {
        var pointer = PathFor(projectDirectory);
        if (!File.Exists(pointer))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(pointer));
            var url = document.RootElement.GetProperty("url").GetString();
            var config = document.RootElement.GetProperty("config").GetString();

            return !string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(config) && File.Exists(config)
                ? new IslandDevPointer(url, config)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Half-written, or written by an older Rask. The caller looks again.
            return null;
        }
    }

    /// <summary>
    ///     Waits for the build to drop the pointer, or gives up.
    /// </summary>
    /// <remarks>
    ///     Polled rather than watched: the file appears exactly once per session, within the first build, and
    ///     a FileSystemWatcher for that is more moving parts than the thing it replaces. The caller's timeout
    ///     should be generous, because the first build of a clean clone restores and compiles first — and
    ///     giving up quietly is right, since the app is running by then and all that is lost is hot reload.
    /// </remarks>
    internal static async Task<IslandDevPointer?> WaitAsync(
        string projectDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (TryRead(projectDirectory) is { } pointer)
            {
                return pointer;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }
}
