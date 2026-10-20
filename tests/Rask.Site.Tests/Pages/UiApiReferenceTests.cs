using System.Reflection;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Tests.Pages;

/// <summary>
///     The UI tab's reference tables are written out by hand, and these hold them to the components: a step the kit
///     gains is missing from the table, and a step it loses is a row about nothing.
/// </summary>
public sealed class UiApiReferenceTests
{
    // Every component writes these, and the reference says so once rather than in every table.
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal) { "Class", "Id", "Key", "Children" };

    public static TheoryData<string> Parts()
    {
        var data = new TheoryData<string>();
        foreach (var part in UiApi.All)
        {
            data.Add(part.Entry);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void Every_step_a_component_has_is_in_its_table_and_every_row_is_a_step(string entry)
    {
        var part = UiApi.All.Single(candidate => candidate.Entry == entry);

        var steps = part.Component
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.SetMethod is { IsPublic: true } && !Common.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var rows = part.Steps.ToDictionary(step => step.Name, step => step.Takes, StringComparer.Ordinal);

        Assert.Equal(steps.Keys.Order(StringComparer.Ordinal), rows.Keys.Order(StringComparer.Ordinal));
        Assert.All(rows, row => Assert.Equal(steps[row.Key], row.Value));
    }

    [Fact]
    public void Every_part_names_the_flux_part_its_component_mirrors()
    {
        var parts = UiApi.All.ToList();

        Assert.All(parts, part => Assert.StartsWith("flux:", part.Flux, StringComparison.Ordinal));
        Assert.All(parts, part => Assert.Equal("Ui" + part.Entry["Ui.".Length..], part.Component.Name.Split('`')[0]));
        Assert.Equal(parts.Count, parts.Select(part => part.Entry).Distinct(StringComparer.Ordinal).Count());
    }
}
