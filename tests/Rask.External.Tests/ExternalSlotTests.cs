using System.Text;

namespace Rask.External.Tests;

/// <summary>A panel whose chrome is React and whose contents stay Rask's.</summary>
public sealed partial class Panel : ReactComponent
{
    /// <summary>The panel's heading.</summary>
    public string? Heading { get; set; }
}

// Slots are what make an island a REPLACEMENT rather than an embed: swapping a component in the
// middle of a tree must not strand its descendants. The content stays Rask-rendered — it is only
// where those nodes end up that the island's own framework decides.
public partial class ExternalSlotTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unassigned_children_go_to_the_default_slot()
    {
        var html = Render(Panel.Heading("Sales")[Span["row one"]]);

        Assert.Contains("<template data-rask-slot=\"default\">", html, StringComparison.Ordinal);
        Assert.Contains("<span>row one</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_slot_gets_its_own_template()
    {
        var html = Render(Panel.Heading("Sales")[
            ExternalSlot.Named("footer")[Span["saved"]],
            Span["row one"]
        ]);

        Assert.Contains("<template data-rask-slot=\"footer\">", html, StringComparison.Ordinal);
        Assert.Contains("<template data-rask-slot=\"default\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_slot_comes_first_whatever_order_it_was_written_in()
    {
        // The client lifts templates by name, so order is not load-bearing for correctness — but a
        // stable order keeps the rendered HTML diffable and makes the output readable.
        var html = Render(Panel.Heading("Sales")[
            ExternalSlot.Named("footer")[Span["saved"]],
            Span["row one"]
        ]);

        Assert.True(
            html.IndexOf("data-rask-slot=\"default\"", StringComparison.Ordinal)
            < html.IndexOf("data-rask-slot=\"footer\"", StringComparison.Ordinal),
            $"expected the default slot first:\n{html}");
    }

    [Fact]
    public void Slot_content_is_inert_until_the_island_mounts()
    {
        // Inside a <template>, so the browser parses it but never renders it. Without that, slot
        // content would paint at first paint and then jump when the adapter relocated it.
        var html = Render(Panel.Heading("Sales")[Span["row one"]]);

        var slotStart = html.IndexOf("<template", StringComparison.Ordinal);
        var contentStart = html.IndexOf("<span>row one</span>", StringComparison.Ordinal);
        Assert.True(slotStart >= 0 && contentStart > slotStart, $"content escaped its template:\n{html}");
    }

    [Fact]
    public void A_leaf_island_still_emits_no_children_at_all()
    {
        // The regression guard for P0: an island with no content must render exactly as it did before
        // slots existed, and pay nothing for them.
        var html = Render(Panel.Heading("Sales"));

        Assert.EndsWith("></rask-external>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<template", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_island_keeps_its_diff_boundary_with_slots_present()
    {
        var html = Render(Panel.Heading("Sales")[Span["row one"]]);

        Assert.Contains("data-rask-opaque", html, StringComparison.Ordinal);
    }

    private static string Render(Component component)
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(component, sb);
        return sb.ToString();
    }
}
