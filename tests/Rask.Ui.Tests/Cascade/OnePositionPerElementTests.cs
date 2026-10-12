using System.Text.RegularExpressions;
using Rask.UiTests.Flux;

namespace Rask.UiTests.Cascade;

/// <summary>
///     An element the kit draws asks for one <c>position</c>. Two position utilities on one element leave the
///     winner to the order Tailwind prints them in, and <c>relative</c> is printed after <c>absolute</c>.
/// </summary>
/// <remarks>
///     How a time picker's clear button came to be drawn under its trigger: its class joined
///     <c>absolute top-0 bottom-0 end-9</c> to a look that begins with <c>relative</c>. Every parity page is
///     read, which is every component in every state Flux's docs show and the ones
///     <see cref="Flux.Parity.ClearPlacementParity" /> adds.
/// </remarks>
public sealed partial class OnePositionPerElementTests
{
    private static readonly HashSet<string> Positions = new(StringComparer.Ordinal) { "static", "fixed", "absolute", "relative", "sticky" };

    [Fact]
    public void No_element_on_a_parity_page_asks_for_two_positions_at_once()
    {
        var pages = typeof(FluxParity).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(FluxParity)))
            .Select(type => (FluxParity)Activator.CreateInstance(type)!)
            .ToList();

        var twice = pages
            .SelectMany(page => page.Examples())
            .SelectMany(example => ClassAttribute().Matches(example.Example.ToHtml()).Select(match => match.Groups[1].Value))
            .Where(classes => classes.Split(' ').Count(Positions.Contains) > 1)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(pages);
        Assert.Empty(twice);
    }

    [GeneratedRegex("\\sclass=\"([^\"]*)\"")]
    private static partial Regex ClassAttribute();
}
