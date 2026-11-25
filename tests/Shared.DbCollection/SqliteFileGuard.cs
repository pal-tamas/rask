using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Rask.Tests.Shared;

/// <summary>
///     Asserts that no test in a suite puts its SQLite database in a file unless its file is named as one that is ABOUT
///     a file — so the suite's databases live in memory, each test's its own, and its classes run side by side.
/// </summary>
/// <remarks>
///     <para>
///         A database in a temporary file was the old shape: every class created one, deleted it on dispose, and the
///         suite ran its database classes one at a time in a collection of their own. In memory
///         (<c>InMemorySqlite</c>, <c>TestDatabase.Start&lt;T&gt;()</c>) there is no file to share or to leak, so the
///         collection went with it — and the guard watches for the shape coming back, which a new test copied from an
///         old one would bring.
///     </para>
///     <para>
///         It reads the suite's own source, because "does this test open a file?" is a question about what the code
///         spells: a <c>.db</c> name, a temporary file. A file listed here is a judgement somebody made — a backup, a
///         database that must survive a reopen — and the reason is written beside its name at the call site.
///     </para>
/// </remarks>
public static partial class SqliteFileGuard
{
    /// <summary>Fails when a source file of the calling suite spells a SQLite file and is not named in <paramref name="allowed" />.</summary>
    /// <param name="allowed">The suite's files, by name, whose tests are about a database file.</param>
    /// <param name="here">The calling source file, which places the suite's directory; filled in by the compiler.</param>
    public static void AssertNoTestOpensASqliteFile(string[] allowed, [CallerFilePath] string here = "")
    {
        ArgumentNullException.ThrowIfNull(allowed);

        var project = ProjectDirectoryOf(here);
        var sources = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .ToDictionary(Path.GetFileName, path => path, StringComparer.Ordinal);

        var stale = allowed.Where(name => !sources.ContainsKey(name)).ToList();
        Assert.True(
            stale.Count == 0,
            $"The allow-list names files that no longer exist: {string.Join(", ", stale)}. Drop them.");

        var opening = sources
            .Where(source => !allowed.Contains(source.Key, StringComparer.Ordinal))
            .Where(source => SqliteFile().IsMatch(File.ReadAllText(source.Value)))
            .Select(source => source.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            opening.Count == 0,
            $"These files put a SQLite database in a file: {string.Join(", ", opening)}. "
            + "Give the test a database in memory (InMemorySqlite, or TestDatabase.Start<T>()), "
            + "or — when the test is about the file itself — name the file in this guard's allow-list, with the reason.");
    }

    // A database file's name (`app.db`, `…{Guid}.db`), or a temporary file to hold one.
    [GeneratedRegex(@"\.(db|sqlite)""|GetTempFileName\(", RegexOptions.CultureInvariant)]
    private static partial Regex SqliteFile();

    private static string ProjectDirectoryOf(string here)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (directory is not null && directory.GetFiles("*.csproj").Length == 0)
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException($"No project above {here}.");
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var segments
        && (segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal));
}
