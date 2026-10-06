using System.Text.Json;

namespace Rask.Core.Tests.Dom;

/// <summary>
/// The snapshot's <c>css</c> section: what <c>scripts/mdn/css.mjs</c> reduces each property's value grammar to.
/// These read the committed data, so a refresh that changes how a grammar is read fails here, by name.
/// </summary>
public sealed class CssSnapshotTests
{
    private static JsonElement Properties => MdnSnapshot.Root.GetProperty("css").GetProperty("properties");

    [Fact]
    public void A_property_lists_the_keywords_that_are_a_whole_value_alone()
    {
        var position = Property("position");

        var keywords = Strings(position, "keywords");

        Assert.Equal(["static", "relative", "absolute", "sticky", "fixed"], keywords);
    }

    [Fact]
    public void A_keyword_reached_through_a_named_type_is_the_property_s_own()
    {
        var display = Property("display");

        var keywords = Strings(display, "keywords");

        Assert.Contains("grid", keywords);
        Assert.Contains("inline-flex", keywords);
        Assert.Contains("none", keywords);
    }

    [Fact]
    public void A_keyword_that_only_means_something_beside_another_is_left_out()
    {
        var font = Property("font");

        var keywords = Strings(font, "keywords");

        // `font: bold` is not a font; `font: caption` is.
        Assert.DoesNotContain("bold", keywords);
        Assert.Contains("caption", keywords);
    }

    [Fact]
    public void A_property_names_the_value_types_it_takes_alone()
    {
        var width = Property("width");
        var color = Property("color");

        var accepted = (Strings(width, "accepts"), Strings(color, "accepts"), Strings(color, "keywords"));

        Assert.Equal(["length", "percentage"], accepted.Item1);
        Assert.Equal(["color"], accepted.Item2);
        Assert.Empty(accepted.Item3);
    }

    [Fact]
    public void The_keywords_every_property_takes_are_in_no_property_s_own_set()
    {
        string[] cssWide = ["inherit", "initial", "unset", "revert", "revert-layer"];

        var carrying = Properties.EnumerateArray()
            .Where(p => Strings(p, "keywords").Intersect(cssWide, StringComparer.Ordinal).Any())
            .Select(Name).ToList();

        Assert.Empty(carrying);
    }

    [Fact]
    public void No_vendor_prefixed_name_is_a_property()
    {
        var names = Properties.EnumerateArray().Select(Name).ToList();

        var prefixed = names.Where(n => n.StartsWith('-')).ToList();

        Assert.Empty(prefixed);
        Assert.True(names.Count > 300, $"only {names.Count} properties");
    }

    [Fact]
    public void Every_property_says_where_it_ships_and_links_to_its_spec()
    {
        var properties = Properties.EnumerateArray().ToList();

        var bare = properties
            .Where(p => p.GetProperty("support").EnumerateObject().Count() < 2
                        || !p.GetProperty("spec").GetString()!.StartsWith("https://", StringComparison.Ordinal))
            .Select(Name).ToList();

        Assert.Empty(bare);
    }

    [Fact]
    public void The_snapshot_pins_the_release_the_grammars_were_read_from()
    {
        var sources = MdnSnapshot.Root.GetProperty("sources");

        var version = sources.GetProperty("@webref/css").GetString();

        Assert.Matches(@"^\d+\.\d+\.\d+$", version!);
    }

    private static JsonElement Property(string name) => Properties.EnumerateArray().Single(p => Name(p) == name);

    private static string Name(JsonElement property) => property.GetProperty("name").GetString()!;

    private static List<string> Strings(JsonElement property, string member) =>
        property.GetProperty(member).EnumerateArray().Select(e => e.GetString()!).ToList();
}
