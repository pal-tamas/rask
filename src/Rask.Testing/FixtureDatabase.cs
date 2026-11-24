using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Testing;

/// <summary>
///     Finds the <c>TestDatabase</c> a test class's fixtures started — a fixture that is one, or holds one as a public
///     property — and binds it to the test.
/// </summary>
/// <remarks>
///     By name rather than by type: Rask.Testing needs no EF Core, so it does not reference Rask.Data, which a test that
///     starts a database does.
/// </remarks>
internal static class FixtureDatabase
{
    private const string TestDatabaseType = "Rask.Data.TestDatabase";

    /// <summary>Binds the first database among <paramref name="fixtures" /> and their public properties; null when none.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Test fixtures live in a test assembly, which is run as built and never trimmed.")]
    public static IDisposable? Bind(IEnumerable<object> fixtures)
    {
        foreach (var fixture in fixtures)
        {
            var database = IsTestDatabase(fixture.GetType())
                ? fixture
                : fixture.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static p => IsTestDatabase(p.PropertyType) && p.GetIndexParameters().Length == 0)
                    .Select(p => p.GetValue(fixture))
                    .FirstOrDefault(static value => value is not null);
            if (database is not null)
            {
                return database.GetType().GetMethod("Bind", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)?
                    .Invoke(database, null) as IDisposable;
            }
        }

        return null;
    }

    private static bool IsTestDatabase(Type? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, TestDatabaseType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
