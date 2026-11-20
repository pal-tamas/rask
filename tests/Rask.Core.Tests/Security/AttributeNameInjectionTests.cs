using Rask.Core.Caching;
using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // Cached is built directly to key it without a render

namespace Rask.Core.Tests.Security;

// A bag key is an attribute's NAME, which HTML has no escape for: one taken from input with a space or a quote in it
// used to end the name and write attributes of the sender's own. A key outside the grammar is not written at all.
public partial class AttributeNameInjectionTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("x onmouseover=alert(1)")]
    [InlineData("x\"><script>alert(1)</script>")]
    [InlineData("a'b")]
    [InlineData("a/b")]
    [InlineData("a=b")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    public void A_data_key_that_is_no_attribute_name_is_not_written(string key)
    {
        var html = Div.Data((key, "1"), ("kept", "2")).ToHtml();

        Assert.Equal("<div data-kept=\"2\"></div>", html);
    }

    [Fact]
    public void An_aria_key_that_is_no_attribute_name_is_not_written()
    {
        var html = Span.Aria(("label onfocus=alert(1)", "x"), ("label", "Close")).ToHtml();

        Assert.Equal("<span aria-label=\"Close\"></span>", html);
    }

    [Fact]
    public void A_verbatim_attribute_key_that_is_no_attribute_name_is_not_written_and_an_empty_one_neither()
    {
        var html = Div.Attributes(new Dictionary<string, string?> { ["x onclick"] = "1", [""] = "2", ["lang"] = "fr" }).ToHtml();

        Assert.Equal("<div lang=\"fr\"></div>", html);
    }

    [Fact]
    public void A_rask_key_and_names_outside_ascii_are_still_names()
    {
        var html = Div.Data(("rask-thing", "1"), ("név", "2")).ToHtml();

        Assert.Equal("<div data-rask-thing=\"1\" data-név=\"2\"></div>", html);
    }

    [Fact]
    public void A_live_frame_carries_no_attribute_the_markup_left_out()
    {
        var writer = new FrameWriter();

        using (FrameSinkScope.Push(writer))
        {
            _ = Div.Data("x y", "1").ToHtml();
        }

        Assert.DoesNotContain(writer.WrittenSpan.ToArray(), f => f.Name?.Contains(' ', StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_session_is_named_in_a_log_line_by_a_short_digest_never_by_its_id()
    {
        const string id = "3f1c8a7e-0d2b-4c55-9e61-7b2a90d4e1aa";

        var named = RenderPace.Named(id);

        Assert.Equal(8, named.Length);
        Assert.DoesNotContain(named, id, StringComparison.Ordinal);
        Assert.Equal(named, RenderPace.Named(id));
        Assert.NotEqual(named, RenderPace.Named(id + "x"));
    }

    [Fact]
    public void A_cached_fragment_refuses_to_vary_by_a_value_whose_text_is_only_its_type_name()
    {
        var fragment = new Cached { Name = "customer", VaryBy = new Customer(1) };

        var ex = Assert.Throws<InvalidOperationException>(() => FragmentKeys.For(fragment, services: null));

        Assert.Contains(nameof(Customer), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tuple_holding_such_a_value_is_refused_too()
    {
        var fragment = new Cached { Name = "customer", VaryBy = (7, new Customer(1)) };

        Assert.Throws<InvalidOperationException>(() => FragmentKeys.For(fragment, services: null));
    }

    [Fact]
    public void Numbers_strings_records_and_tuples_of_them_key_apart()
    {
        string? Key(object value) => FragmentKeys.For(new Cached { Name = "n", VaryBy = value }, services: null);

        var keys = new[] { Key(7), Key("7a"), Key(new Order(1)), Key(new Order(2)), Key((1, "a")), Key((1, "b")) };

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    private sealed class Customer(int id)
    {
        public int Id { get; } = id;
    }

    private sealed record Order(int Id);
}
