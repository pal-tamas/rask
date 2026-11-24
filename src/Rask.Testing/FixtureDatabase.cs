using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Data;

namespace Rask.Testing;

/// <summary>Finds the <see cref="TestDatabase" /> a test class's fixtures started — a fixture that is one, or holds one.</summary>
internal static class FixtureDatabase
{
    /// <summary>The first database among <paramref name="fixtures" /> and their public properties, or null.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Test fixtures live in a test assembly, which is run as built and never trimmed.")]
    public static TestDatabase? In(IEnumerable<object> fixtures)
    {
        foreach (var fixture in fixtures)
        {
            if (fixture is TestDatabase database)
            {
                return database;
            }

            var held = fixture.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static p => typeof(TestDatabase).IsAssignableFrom(p.PropertyType) && p.GetIndexParameters().Length == 0)
                .Select(p => p.GetValue(fixture))
                .OfType<TestDatabase>()
                .FirstOrDefault();
            if (held is not null)
            {
                return held;
            }
        }

        return null;
    }
}
