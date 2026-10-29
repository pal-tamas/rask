namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A child collection behind a field EF Core cannot pair with the property by name is declared to the runtime, on the
/// aggregate itself or on a generic abstract base; one EF Core finds on its own is left to it.
/// </summary>
public class ChildFieldDeclarationTests
{
    private const string Books = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Courts;
        public sealed class Note : Entity<int>
        {
            private Note() { }
            public string Text { get; private set; } = "";
        }
        public sealed class Entry : Entity<int>
        {
            private Entry() { }
            public string Text { get; private set; } = "";
        }
        public abstract class Casebook<TSelf> : Aggregate<int>, IFormModel
            where TSelf : Casebook<TSelf>
        {
            private readonly List<Note> _items = [];
            public IReadOnlyList<Note> Notes => _items.AsReadOnly();
        }
        public sealed class CivilCasebook : Casebook<CivilCasebook>
        {
            private CivilCasebook() { }
        }
        public sealed class Logbook : Aggregate<int>
        {
            private readonly List<Entry> _records = [];
            private Logbook() { }
            public IReadOnlyList<Entry> Entries => _records;
        }
        public sealed class Diary : Aggregate<int>
        {
            private readonly List<Entry> _entries = [];
            private Diary() { }
            public IReadOnlyList<Entry> Entries => _entries;
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Books,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_field_on_a_generic_base_is_declared_for_the_closed_aggregate()
    {
        var run = Run();

        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "DeclareChildField(typeof(global::Courts.CivilCasebook), \"Notes\", \"_items\");", conventions, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_on_the_aggregate_itself_is_declared()
    {
        var run = Run();

        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.Contains(
            "DeclareChildField(typeof(global::Courts.Logbook), \"Entries\", \"_records\");", conventions, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_named_the_way_ef_core_pairs_it_is_left_to_ef_core()
    {
        var run = Run();

        var conventions = run.GeneratedSource("__RaskConventions");

        Assert.DoesNotContain("global::Courts.Diary", conventions, StringComparison.Ordinal);
    }
}
