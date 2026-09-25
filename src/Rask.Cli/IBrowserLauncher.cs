namespace Rask.Cli;

/// <summary>
///     The seam through which <c>rask dev --open</c> opens a browser. Faked in tests so the suite never
///     spawns one.
/// </summary>
internal interface IBrowserLauncher
{
    /// <summary>
    ///     Opens <paramref name="url" />. Returns false if the platform command could not be started —
    ///     never throws, and never affects the exit code: <c>rask dev</c> must not die because a browser
    ///     didn't open.
    /// </summary>
    Task<bool> TryOpenAsync(string url, CancellationToken cancellationToken);
}
