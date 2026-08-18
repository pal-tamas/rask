namespace Rask.Html.Tests.Components;

/// <summary>
///     The global attributes every element carries (#693). Before these landed, everything MDN lists as
///     global beyond id/class/style/title/data-*/role/tabindex/aria-*/draggable was unreachable — not
///     verbose, impossible, because there was no escape hatch either.
/// </summary>
public partial class GlobalAttributeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Lang_marks_a_run_of_text_in_another_language() =>
        // WCAG 3.1.2 (Language of Parts): without this a screen reader reads a French phrase with English
        // phonetics. `lang` existed on <html> only, so the page language worked and a phrase did not.
        // Text encodes non-ASCII to numeric entities, hence the escaped form — unrelated to `lang`, but
        // the French phrase is the point: this is the case `lang` on <html> alone could not express.
        Assert.Equal("<span lang=\"fr\">d&#xE9;j&#xE0; vu</span>", Span.Lang("fr")["déjà vu"].ToHtml());

    [Fact]
    public void Dir_marks_direction_on_any_element() =>
        Assert.Equal("<p dir=\"auto\">x</p>", P.Dir("auto")["x"].ToHtml());

    [Fact]
    public void Hidden_and_Inert_are_bare_boolean_attributes()
    {
        Assert.Equal("<div hidden></div>", Div.Hidden(true).ToHtml());
        Assert.Equal("<div inert></div>", Div.Inert(true).ToHtml());
    }

    [Fact]
    public void Hidden_and_Inert_emit_nothing_when_false_or_unset()
    {
        // Presence IS the value for a bare boolean attribute, so `false` must not render `hidden="false"`.
        Assert.Equal("<div></div>", Div.Hidden(false).ToHtml());
        Assert.Equal("<div></div>", Div.Inert(false).ToHtml());
        Assert.Equal("<div></div>", Div.ToHtml());
    }

    [Fact]
    public void Spellcheck_and_Translate_render_their_enumerated_values()
    {
        // Enumerated, not boolean-presence: false has to render explicitly, and `translate` spells its
        // values yes/no rather than true/false.
        Assert.Equal("<div spellcheck=\"false\"></div>", Div.Spellcheck(false).ToHtml());
        Assert.Equal("<div spellcheck=\"true\"></div>", Div.Spellcheck(true).ToHtml());
        Assert.Equal("<div translate=\"no\"></div>", Div.Translate(false).ToHtml());
        Assert.Equal("<div translate=\"yes\"></div>", Div.Translate(true).ToHtml());
    }

    [Fact]
    public void Popover_and_ContentEditable_carry_their_enumerated_values()
    {
        Assert.Equal("<div popover=\"auto\"></div>", Div.Popover("auto").ToHtml());
        Assert.Equal("<div contenteditable=\"plaintext-only\"></div>",
            Div.ContentEditable("plaintext-only").ToHtml());
    }

    [Fact]
    public void Attributes_reaches_anything_the_surface_does_not_name() =>
        // The escape hatch: microdata has no typed properties and never will.
        Assert.Equal("<div itemscope=\"\" itemtype=\"https://schema.org/Person\"></div>",
            Div.Attributes(new Dictionary<string, string?>
            {
                ["itemscope"] = string.Empty,
                ["itemtype"] = "https://schema.org/Person",
            }).ToHtml());

    [Fact]
    public void Attributes_encodes_its_values_and_writes_keys_verbatim() =>
        Assert.Equal("<div data-raw=\"a&amp;b&lt;c\"></div>",
            Div.Attributes(new Dictionary<string, string?> { ["data-raw"] = "a&b<c" }).ToHtml());

    [Fact]
    public void Global_attributes_render_in_the_documented_order() =>
        // The invariant the whole element surface is asserted against: id, class, style, title, the plain
        // globals, data-*, role, tabindex, aria-*, then the Attributes escape hatch, then tag-specifics.
        // Written in a deliberately scrambled order to prove the ORDER comes from the renderer.
        Assert.Equal(
            "<a id=\"i\" class=\"c\" style=\"s\" title=\"t\" lang=\"en\" dir=\"ltr\" hidden inert "
            + "popover=\"auto\" contenteditable=\"true\" spellcheck=\"true\" translate=\"no\" "
            + "data-k=\"v\" role=\"link\" tabindex=\"0\" aria-label=\"l\" itemprop=\"url\" href=\"/x\"></a>",
            A.Href("/x")
                .Attributes(new Dictionary<string, string?> { ["itemprop"] = "url" })
                .Aria(new Dictionary<string, string?> { ["label"] = "l" })
                .TabIndex(0)
                .Role("link")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .Translate(false)
                .Spellcheck(true)
                .ContentEditable("true")
                .Popover("auto")
                .Inert(true)
                .Hidden(true)
                .Dir("ltr")
                .Lang("en")
                .Title("t")
                .Style("s")
                .Class("c")
                .Id("i")
                .ToHtml());

    [Fact]
    public void An_element_naming_no_global_renders_exactly_as_before() =>
        // The whole point of the flag-bit/LiveState storage: adding ten properties must cost the common
        // element nothing, in output or in footprint.
        Assert.Equal("<div class=\"card\"><span>hi</span></div>", Div.Class("card")[Span["hi"]].ToHtml());

    [Fact]
    public void Hidden_and_Inert_share_the_flags_byte_with_Component_and_must_not_alias_it()
    {
        // Hidden/Inert are stored in `_flags`, which Element does NOT own alone: Component takes bit 0
        // (ReadsAmbientState) and bit 3 (CallbackAssigned), Element's Draggable takes bits 1-2, and the
        // two sets of constants live in different files. Overlapping them is an easy and SILENT mistake:
        // this branch originally gave Hidden bit 3, and everything still rendered correctly, because the
        // damage is to what the flag MEANS rather than to the markup.
        //
        // So assert the meaning, not the output — rendering `<div hidden>` passes either way and is why
        // an output-only version of this test is worthless. Setting Hidden must not make the element
        // claim it carries a callback (which would run the ~88-field eager delegate reset on every
        // hidden element), and assigning a callback must not give Hidden a value it was never set to.
        var hidden = (Element)Div.Hidden(true).Inert(true);
        Assert.False(hidden.HasCallbackAssignedInternal());

        var clickable = (Element)Div.OnClick(() => { });
        Assert.True(clickable.HasCallbackAssignedInternal());
        Assert.Null(clickable.Hidden);
        Assert.Null(clickable.Inert);

        // Draggable shares the same byte from the other side.
        var draggable = (Element)Div.Draggable(true);
        Assert.Null(draggable.Hidden);
        Assert.Null(draggable.Inert);

        // And the rendered output still has to be right with all of them at once. Draggable renders in
        // the data-* group, so it lands AFTER the plain globals despite sharing the byte with them.
        Assert.Equal("<div hidden inert draggable=\"true\"></div>",
            Div.Draggable(true).Hidden(true).Inert(true).ToHtml());
    }
}
