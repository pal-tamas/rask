using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     The CSS layer order the kit's stylesheet establishes for the whole document.
/// </summary>
/// <remarks>
///     <para>
///     A browser orders layers by FIRST APPEARANCE, across every sheet on the page, in link order — and
///     nothing later can reorder a name that has already been placed. The kit's sheet is linked first,
///     so whatever order it happens to mention layers in becomes the order every OTHER sheet is ranked
///     by, including the consuming app's own Tailwind output.
///     </para>
///     <para>
///     Left implicit, that order came out as <c>properties, theme, utilities, daisyui, rask, base</c> —
///     daisyUI emits its <c>base</c> rules last — which put <c>base</c> ABOVE <c>utilities</c> for the
///     entire page. The app's own <c>@layer theme, base, components, utilities;</c> was then a no-op,
///     because those names were already ordered, and its Tailwind preflight outranked every utility it
///     had emitted. On rask.sh that rendered the whole site as near-unstyled HTML with every class name
///     present and correct in the markup, on a green build: <c>h1.text-4xl.font-semibold</c> computed to
///     16px/400 against preflight's <c>h1..h6 { font-size: inherit; font-weight: inherit }</c>, and
///     <c>px-5</c> computed to <c>padding: 0</c> against <c>*{padding:0}</c>.
///     </para>
///     <para>
///     These assert on the COMPILED artifact, not on the <c>@layer</c> line in ui.css: Tailwind rewrites
///     that statement while emitting its own blocks, so the source stating an intention is not evidence
///     the bytes an app receives carry it.
///     </para>
/// </remarks>
public sealed class UiLayerOrderTests
{
    /// <summary>Every layer name the sheet mentions, in order of first appearance.</summary>
    /// <remarks>
    ///     Both spellings count and both place a name: the statement <c>@layer a, b;</c> and the block
    ///     <c>@layer a { … }</c>. Reading only one of the two is how this stayed invisible.
    /// </remarks>
    private static List<string> DeclaredOrder()
    {
        var order = new List<string>();
        foreach (Match m in Regex.Matches(UiStylesheet.Css, @"@layer\s+([A-Za-z0-9_\-]+(?:\s*,\s*[A-Za-z0-9_\-]+)*)\s*[{;]"))
        {
            foreach (var name in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!order.Contains(name, StringComparer.Ordinal))
                {
                    order.Add(name);
                }
            }
        }

        return order;
    }

    [Fact]
    public void The_sheet_declares_a_layer_order_at_all()
    {
        // Vacuous-pass guard: if the regex ever stops matching, every ordering assertion below would
        // silently hold over an empty list.
        var order = DeclaredOrder();
        Assert.Contains("utilities", order);
        Assert.Contains("base", order);
    }

    [Theory]
    // A consuming app's utilities must beat its own preflight. This is the pair that broke.
    [InlineData("base", "utilities")]
    // …and they must beat both daisyUI and the kit's own corrections, which is the "an app keeps its
    // own cascade" promise the layer() imports in ui.css are already making.
    [InlineData("daisyui", "utilities")]
    [InlineData("rask", "utilities")]
    // The kit's corrections exist precisely to outrank the library they correct.
    [InlineData("daisyui", "rask")]
    // Tokens have to be defined before the reset that reads them.
    [InlineData("theme", "base")]
    public void Layer_is_ordered_before(string lower, string higher)
    {
        var order = DeclaredOrder();
        var lowerAt = order.IndexOf(lower);
        var higherAt = order.IndexOf(higher);

        Assert.True(lowerAt >= 0, $"the compiled sheet never mentions the '{lower}' layer; order was: {string.Join(", ", order)}");
        Assert.True(higherAt >= 0, $"the compiled sheet never mentions the '{higher}' layer; order was: {string.Join(", ", order)}");
        Assert.True(
            lowerAt < higherAt,
            $"'{lower}' must be ordered before '{higher}' so that '{higher}' wins, but the compiled sheet "
            + $"places them as: {string.Join(", ", order)}. The kit's sheet is linked first, so this order "
            + "is the one every sheet on the page is ranked by — see the remarks on this class.");
    }
}
