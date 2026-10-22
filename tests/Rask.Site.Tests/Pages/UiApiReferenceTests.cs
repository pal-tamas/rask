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

        var steps = KitTypes(part.Component)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod is { IsPublic: true } && !Common.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var methods = ChainMethods(part.Component).ToHashSet(StringComparer.Ordinal);
        var rows = part.Steps.ToDictionary(step => step.Name, step => step.Takes, StringComparer.Ordinal);

        Assert.Equal(steps.Keys.Concat(methods).Order(StringComparer.Ordinal), rows.Keys.Order(StringComparer.Ordinal));
        Assert.All(rows.Where(row => !methods.Contains(row.Key)), row => Assert.Equal(steps[row.Key], row.Value));
    }

    // A step written as a method on the component — `Ui.Input.Bind(() => m.Amount).Money()` — hands the component back.
    private static IEnumerable<string> ChainMethods(Type component) =>
        component.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.ReturnType == component)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal);

    // The component and the kit's own bases under it (a select's steps live on UiSelectControl), down to Rask's Component.
    private static IEnumerable<Type> KitTypes(Type component)
    {
        for (var type = component; type is not null && type.Assembly != typeof(Component).Assembly; type = type.BaseType)
        {
            yield return type;
        }
    }

    [Fact]
    public void Every_part_names_the_flux_part_its_component_mirrors()
    {
        var parts = UiApi.All.ToList();

        // The kit's own parts, which the owner asked for, say so in place of a Flux part (FluxConformanceTests.Approved).
        Assert.All(parts, part => Assert.True(
            part.Flux.StartsWith("flux:", StringComparison.Ordinal) || part.Flux == UiApi.NotFlux, part.Entry + " names no Flux part"));
        Assert.All(parts, part => Assert.Equal("Ui" + part.Entry["Ui.".Length..], part.Component.Name.Split('`')[0]));
        Assert.Equal(parts.Count, parts.Select(part => part.Entry).Distinct(StringComparer.Ordinal).Count());
    }
}
