namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK111: a collection of value objects needs a list behind it for EF Core to write it as a NOT NULL JSON column. A
/// get-only <c>IReadOnlyList&lt;T&gt;</c> auto-property has none, and is warned about; the field behind a view, a list
/// property and a collection of plain values are not.
/// </summary>
public class NoListBehindGeneratorTests
{
    private static GeneratorRun Run(string members) =>
        GeneratorHarness.Run(
            $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;

            public sealed record FiledFile(string Name, int Pages);

            public sealed class ReturnFiling : Aggregate<int>
            {
                private ReturnFiling() { }
                {{members}}
            }
            """,
            new ModelRegistryGenerator(),
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_get_only_read_only_list_of_value_objects_is_RASK111()
    {
        var run = Run("public IReadOnlyList<FiledFile> Files { get; private set; } = new List<FiledFile>();");

        var diagnostic = Assert.Single(run.Diagnostics, static d => d.Id == "RASK111");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(
            "'Files' on 'ReturnFiling' has no list behind it, so its JSON column is NULL whatever its nullability; hold it in a List<FiledFile> field",
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private readonly List<FiledFile> _files = []; public IReadOnlyList<FiledFile> Files => _files;")]
    [InlineData("public List<FiledFile> Files { get; private set; } = [];")]
    [InlineData("public List<FiledFile>? Files { get; private set; }")]
    [InlineData("public IReadOnlyList<string> Labels { get; private set; } = new List<string>();")]
    public void A_collection_with_a_list_behind_it_or_of_plain_values_is_fine(string members)
    {
        var run = Run(members);

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK111");
    }
}
