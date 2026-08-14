namespace Rask.SQLite.Crdt;

/// <summary>Where the cr-sqlite extension lives and which tables become replicated relations.</summary>
public sealed class RaskCrdtOptions
{
    /// <summary>
    ///     Path to the cr-sqlite loadable extension — <c>crsqlite.dylib</c>, <c>.so</c> or <c>.dll</c>.
    /// </summary>
    /// <remarks>
    ///     Supplied rather than bundled: cr-sqlite ships a separate native binary per platform, and which
    ///     one is right depends on where the app runs, not on which package it referenced.
    /// </remarks>
    public string ExtensionPath { get; set; } = string.Empty;

    /// <summary>
    ///     Entity types to promote to conflict-free replicated relations. Empty means every entity type
    ///     in the model.
    /// </summary>
    public IList<string> Tables { get; } = [];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExtensionPath))
        {
            throw new InvalidOperationException(
                $"{nameof(RaskCrdtOptions)}.{nameof(ExtensionPath)} is required — set it to the cr-sqlite " +
                "loadable extension for this platform (crsqlite.dylib / .so / .dll).");
        }
    }
}
