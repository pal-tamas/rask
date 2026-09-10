namespace Rask.Ui.Tests;

/// <summary>
///     The repository root, found by walking up from the test binaries to <c>Rask.slnx</c>.
/// </summary>
/// <remarks>
///     Several tests here assert on files in the SOURCE tree rather than on the compiled kit — the
///     vendored daisyUI bundle, <c>ui.css</c>, <c>Rask.Ui.targets</c> — because those are the artifacts
///     whose agreement is the thing under test. Each had grown its own copy of this walk.
/// </remarks>
internal static class RepoRoot
{
    /// <summary>The absolute path of the directory holding <c>Rask.slnx</c>.</summary>
    /// <exception cref="InvalidOperationException">The test binaries are not inside the repository.</exception>
    public static string FullPath { get; } = Find();

    private static string Find()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
