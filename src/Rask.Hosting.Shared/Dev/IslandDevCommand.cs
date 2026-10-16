namespace Rask.Hosting.Shared;

/// <summary>
///     The <c>npx</c> command line that serves a project's islands from a dev server over the config an
///     <see cref="IslandDevPointer" /> names: <c>vp dev</c>.
/// </summary>
/// <remarks>
///     Islands are served by Vite+ alone, as they are bundled by it (<c>_RaskExternalRequireVitePlus</c> fails a
///     build that does not install it, <c>RASKISLAND019</c>). Its <c>vite</c> is an alias to a core that carries
///     no <c>vite</c> command, and npx refuses to run <c>vite</c> from a package that names itself something
///     else — so <c>vp</c> is what serves the config.
/// </remarks>
internal static class IslandDevCommand
{
    /// <summary>The program both readers start.</summary>
    internal const string FileName = "npx";

    /// <summary>The arguments that serve <paramref name="config" />.</summary>
    internal static IReadOnlyList<string> Arguments(string config)
    {
        ArgumentException.ThrowIfNullOrEmpty(config);

        return ["--no-install", "vp", "dev", "--config", config];
    }
}
