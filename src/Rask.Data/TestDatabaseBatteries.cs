using System.Collections.Immutable;

namespace Rask.Data;

/// <summary>The batteries that announced themselves, each once.</summary>
internal static class TestDatabaseBatteries
{
    private static ImmutableArray<ITestDatabaseBattery> _all = [];

    public static ImmutableArray<ITestDatabaseBattery> All => _all;

    /// <summary>Adds <paramref name="battery" />, unless one of its type already is.</summary>
    public static void Add(ITestDatabaseBattery battery) =>
        ImmutableInterlocked.Update(ref _all, static (all, added) =>
            all.Any(b => b.GetType() == added.GetType()) ? all : all.Add(added), battery);
}
