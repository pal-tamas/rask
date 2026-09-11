using Rask.Core;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     A kit element that derives an accessible name from a prop — the shape an icon-only button has.
/// </summary>
internal sealed partial class AriaProbe : UiElement
{
    public string? Name { get; set; }

    protected override string TagName => "span";

    protected override IReadOnlyDictionary<string, string?>? ResolveAria()
    {
        if (Name is null)
        {
            return Aria;
        }

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = Name };
        foreach (var (key, value) in Aria ?? new Dictionary<string, string?>())
        {
            merged[key] = value;
        }

        return merged;
    }
}

/// <summary>
///     <see cref="UiElement.ResolveAria" /> — what the kit adds, where Core would have written it.
/// </summary>
public partial class UiElementTests : RaskMarkup
{
    [Fact]
    public void An_element_that_resolves_nothing_writes_the_call_sites_aria_untouched() =>
        Assert.Equal("<span aria-busy=\"true\"></span>", AriaProbe.Aria(("busy", "true")).ToHtml());

    [Fact]
    public void Resolved_aria_is_written()
    {
        Assert.Equal("<span aria-label=\"Close\"></span>", AriaProbe.Name("Close").ToHtml());
    }

    [Fact]
    public void Resolved_aria_sits_in_cores_aria_slot_not_after_the_escape_hatch()
    {
        // Core's documented order is role, tabindex, aria-*, then Attributes. Appending what the kit derives
        // after the walk would have put it behind the escape hatch.
        var html = AriaProbe
            .Name("Close")
            .Role("img")
            .Attributes(new Dictionary<string, string?> { ["popovertarget"] = "menu" })
            .ToHtml();

        Assert.Equal("<span role=\"img\" aria-label=\"Close\" popovertarget=\"menu\"></span>", html);
    }

    [Fact]
    public void A_label_the_call_site_wrote_wins_and_is_written_once()
    {
        var html = AriaProbe.Name("Close").Aria(("label", "Dismiss the notice")).ToHtml();

        Assert.Equal("<span aria-label=\"Dismiss the notice\"></span>", html);
    }

    [Fact]
    public void Rendering_leaves_the_call_sites_aria_as_it_was()
    {
        // The resolved bag stands in for the call site's only while the attributes are written. A render
        // that left it behind would feed the kit's own label back in as if someone had typed it.
        var probe = AriaProbe.Name("Close");
        var first = probe.ToHtml();

        Assert.Null(probe.Aria);
        Assert.Equal(first, probe.ToHtml());
    }
}
