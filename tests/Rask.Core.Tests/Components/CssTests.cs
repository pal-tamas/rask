using System.Globalization;
using System.Reflection;
using Rask.Core.Tests.Dom;

namespace Rask.Core.Tests.Components;

/// <summary>
///     <c>Css</c>, the typed inline style generated from the snapshot's <c>css</c> section
///     (src/Rask.Dom.Tasks/CssEmitter.cs): each value form, the chain, and its fit with <c>Style</c>.
/// </summary>
public partial class CssTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_length_is_written_with_its_unit()
    {
        Css style = Css.Height(40.Px);

        var text = style.ToString();

        Assert.Equal("height:40px", text);
    }

    [Fact]
    public void A_keyword_is_chosen_from_the_property_s_own_set()
    {
        Css style = Css.Position().Sticky;

        var text = style.ToString();

        Assert.Equal("position:sticky", text);
    }

    [Fact]
    public void A_keyword_of_several_words_is_written_with_its_hyphens()
    {
        Css style = Css.Width().MinContent;

        var text = style.ToString();

        Assert.Equal("width:min-content", text);
    }

    [Fact]
    public void Steps_chain_in_the_order_they_are_written()
    {
        Css style = Css.Position().Sticky.Top(0.Px).Width(50.Percent).Display().Grid;

        var text = style.ToString();

        Assert.Equal("position:sticky;top:0px;width:50%;display:grid", text);
    }

    [Fact]
    public void Any_text_CSS_allows_is_a_value()
    {
        Css style = Css.Width("calc(100% - 2rem)").Color("var(--accent)");

        var text = style.ToString();

        Assert.Equal("width:calc(100% - 2rem);color:var(--accent)", text);
    }

    [Fact]
    public void A_null_text_declares_nothing()
    {
        var hovering = false;

        Css style = Css.Height(2.Rem).Background(hovering ? "#eef6ff" : null);

        Assert.Equal("height:2rem", style.ToString());
    }

    [Fact]
    public void A_style_that_declares_nothing_is_no_style_attribute()
    {
        var html = Div.Style(Css.Background(null))["x"].ToHtml();

        Assert.Equal("<div>x</div>", html);
    }

    [Fact]
    public void Numbers_and_durations_are_written_the_same_in_every_culture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("hu-HU");

        try
        {
            Css style = Css.Opacity(0.5).ZIndex(3).TransitionDuration(150.Milliseconds).Width(1.5.Rem);

            Assert.Equal("opacity:0.5;z-index:3;transition-duration:150ms;width:1.5rem", style.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_style_goes_wherever_a_style_string_does()
    {
        var html = Div.Style(Css.Height(40.Px).Display().Flex)["x"].ToHtml();

        Assert.Equal("<div style=\"height:40px;display:flex\">x</div>", html);
    }

    [Fact]
    public void A_style_kept_in_a_field_is_built_on_without_changing_it()
    {
        Css sticky = Css.Position().Sticky.Top(0.Px);

        Css wide = sticky.Width(110.Px);

        Assert.Equal("position:sticky;top:0px", sticky.ToString());
        Assert.Equal("position:sticky;top:0px;width:110px", wide.ToString());
    }

    [Fact]
    public void Every_property_in_the_snapshot_begins_a_style_and_continues_one()
    {
        var properties = MdnSnapshot.Root.GetProperty("css").GetProperty("properties").EnumerateArray()
            .Select(p => string.Concat(p.GetProperty("name").GetString()!.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..])))
            .ToHashSet(StringComparer.Ordinal);

        var entries = Methods(typeof(Css), BindingFlags.Static);
        var steps = Methods(typeof(CssSteps), BindingFlags.Static);

        Assert.Empty(properties.Except(entries, StringComparer.Ordinal));
        Assert.Empty(properties.Except(steps, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_keyword_in_the_snapshot_is_a_member_of_its_property_s_set()
    {
        var expected = MdnSnapshot.Root.GetProperty("css").GetProperty("properties").EnumerateArray()
            .Sum(p => p.GetProperty("keywords").GetArrayLength());

        var generated = typeof(Css).GetNestedTypes()
            .Sum(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(p => p.PropertyType == typeof(Css)));

        Assert.Equal(expected, generated);
    }

    private static HashSet<string> Methods(Type type, BindingFlags scope) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | scope).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
}
