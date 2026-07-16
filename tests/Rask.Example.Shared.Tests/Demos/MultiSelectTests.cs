using System.Text.Json;
using Rask.Example.Shared;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// The reusable BsMultiSelect<TItem> example component: a custom dropdown bound to an ICollection (bound mode)
// or driven by Value + OnChange (controlled mode). These drive the live handlers directly (open, select,
// deselect, Esc, click-outside) and assert the bound collection / emitted selection / rendered chips. The
// full browser flow is covered in SharedSmokeTests (Multi-select branch).
public sealed class MultiSelectTests
{
    private static readonly string[] Options = ["a", "b", "c"];

    private static (RenderedComponent Page, Bag Model) MountBound()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, Options)],
            TestServices.Default());
        return (page, model);
    }

    [Fact]
    public void Render_Closed_ShowsPlaceholderAndAllOptions()
    {
        var (page, _) = MountBound();
        var html = page.Render();

        Assert.Contains("Select&#x2026;", html);           // default placeholder, HTML-encoded ellipsis
        Assert.DoesNotContain("dropdown-menu show", html);  // closed by default
        Assert.Equal(3, CountOccurrences(html, "dropdown-item"));
        Assert.DoesNotContain("badge", html);               // no chips when empty
    }

    [Fact]
    public async Task Toggle_OpensMenu_AndRendersBackdrop()
    {
        var (page, _) = MountBound();
        var ids = ClickIds(page.Render());

        await page.InvokeAsync(ids[0]); // box toggle is the first click handler

        var html = page.Render();
        Assert.Contains("dropdown-menu show", html);
        Assert.Contains("position-fixed", html);            // click-outside backdrop only when open
    }

    [Fact]
    public async Task ClickOutside_ClosesMenu()
    {
        var (page, _) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        var openIds = ClickIds(page.Render()); // [box, opt-a, opt-b, opt-c, backdrop]
        await page.InvokeAsync(openIds[^1]); // backdrop is the last click handler

        Assert.DoesNotContain("dropdown-menu show", page.Render());
    }

    [Fact]
    public async Task Escape_ClosesMenu()
    {
        var (page, _) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        var keyId = page.HandlerIds("keydown")[0];
        await page.InvokeAsync(keyId, "{\"key\":\"Escape\"}");

        Assert.DoesNotContain("dropdown-menu show", page.Render());
    }

    [Fact]
    public async Task Escape_OtherKey_DoesNotClose()
    {
        var (page, _) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        var keyId = page.HandlerIds("keydown")[0];
        await page.InvokeAsync(keyId, "{\"key\":\"ArrowDown\"}");

        Assert.Contains("dropdown-menu show", page.Render());
    }

    [Fact]
    public async Task ArrowDown_WhenClosed_OpensAndSeedsCursorToFirstOption()
    {
        var (page, _) = MountBound();
        var keyId = page.HandlerIds("keydown")[0]; // closed → the box is the only keydown handler

        await page.InvokeAsync(keyId, "{\"key\":\"ArrowDown\"}");

        var html = page.Render();
        Assert.Contains("dropdown-menu show", html);                              // opened
        Assert.Equal("Tags-opt-0", Markup.Attr(html, "aria-activedescendant")!);  // seeded to the first option
    }

    [Fact]
    public async Task ArrowKeys_HomeEnd_MoveRovingCursor_TrackingActiveDescendant()
    {
        var (page, _) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open (cursor seeded to option 0)

        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"ArrowDown\"}"); // 0 -> 1
        var html = page.Render();
        Assert.Equal("Tags-opt-1", Markup.Attr(html, "aria-activedescendant")!);
        // the highlighted (not selected) option carries .active
        Assert.Contains(
            "<button id=\"Tags-opt-1\" class=\"dropdown-item d-flex align-items-center gap-2 active\"", html);

        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"End\"}"); // -> last
        Assert.Equal("Tags-opt-2", Markup.Attr(page.Render(), "aria-activedescendant")!);

        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"Home\"}"); // -> first
        Assert.Equal("Tags-opt-0", Markup.Attr(page.Render(), "aria-activedescendant")!);
    }

    [Fact]
    public async Task Enter_TogglesCursorOptionMembership()
    {
        var (page, model) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open, cursor seeded to option 0 ("a")

        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"Enter\"}");

        Assert.Equal(["a"], model.Tags);
        var html = page.Render();
        Assert.Contains("badge", html);                        // chip rendered
        Assert.Contains("aria-selected=\"true\"", html);       // the option reflects selection
    }

    [Fact]
    public async Task Space_FromBox_TogglesCursorOption()
    {
        var (page, model) = MountBound();
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open, cursor at option 0

        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\" \"}");

        Assert.Equal(["a"], model.Tags);
    }

    [Fact]
    public async Task Space_InSearchField_TypesSpace_DoesNotToggle()
    {
        // With a Filter, the open dropdown grows a search field. Space there must type a literal space (fall
        // through), never toggle the cursor option — the box handler owns Space-to-toggle, the search doesn't.
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(
                () => model.Tags, Options,
                Filter: (o, t) => o.Contains(t, StringComparison.OrdinalIgnoreCase))],
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open → search field appears

        var keydownIds = page.HandlerIds("keydown"); // [box, search field]
        await page.InvokeAsync(keydownIds[1], "{\"key\":\" \"}"); // Space in the search field

        Assert.Empty(model.Tags);                              // no membership toggled
        Assert.Contains("dropdown-menu show", page.Render());  // still open
    }

    [Fact]
    public async Task SelectOption_AddsToCollection_RendersChipAndCheck()
    {
        var (page, model) = MountBound();
        var ids = ClickIds(page.Render()); // [toggle, opt-a, opt-b, opt-c]

        await page.InvokeAsync(ids[2]); // select "b"

        Assert.Equal(["b"], model.Tags);
        var html = page.Render();
        Assert.Contains("badge", html);                      // chip rendered for the selection
        Assert.Equal(1, CountOccurrences(html, " checked")); // exactly one option checked
    }

    [Fact]
    public async Task RemoveChip_RemovesFromCollection()
    {
        var (page, model) = MountBound();
        model.Tags.Add("a");
        // Layout order: box toggle, then chip-remove buttons (inside the box), then option rows.
        var ids = ClickIds(page.Render()); // [toggle, chip-remove-a, opt-a, opt-b, opt-c]

        await page.InvokeAsync(ids[1]); // remove the "a" chip

        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task RemoveChip_RerendersControl_DropsBadge()
    {
        // Regression: clicking a chip's × is a BsCloseButton (a wrapper) callback, so re-rendering the
        // BsMultiSelect<T> relies on AutoCallback marking it dirty. Because the control is GENERIC, that
        // path used to resolve no owner (DelegateOwner skipped the generic display class holding `this`),
        // so the control never re-rendered and the badge lingered until an unrelated render (reopening the
        // dropdown). A cache-aware second render must now drop the removed chip on its own.
        var (page, model) = MountBound();
        model.Tags.Add("a");
        model.Tags.Add("b");
        var ids = ClickIds(page.Render()); // [toggle, chip-remove-a, chip-remove-b, opt-a, opt-b, opt-c]

        await page.InvokeAsync(ids[1]); // remove the "a" chip
        Assert.Equal(["b"], model.Tags);

        // The cache-aware re-render must reflect the removal without any extra interaction.
        var html = page.Render();
        Assert.Equal(1, CountOccurrences(html, "badge"));     // only the surviving "b" chip
        Assert.Equal(1, CountOccurrences(html, " checked")); // exactly one option still ticked
    }

    [Fact]
    public async Task SelectingTwice_TogglesOff()
    {
        var (page, model) = MountBound();
        var ids = ClickIds(page.Render());

        await page.InvokeAsync(ids[1]); // select "a"
        Assert.Equal(["a"], model.Tags);

        var ids2 = ClickIds(page.Render()); // now [toggle, chip-remove-a, opt-a, opt-b, opt-c]
        await page.InvokeAsync(ids2[2]); // click the "a" option row again
        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task Validate_ShowsMessage_BelowMinimum_AndClearsWhenSatisfied()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[
                BsMultiSelect<string>(
                    () => model.Tags,
                    Options,
                    Validate: tags => tags.Count >= 2 ? Array.Empty<string>() : ["Pick at least two."])],
            TestServices.Default());

        var ids = ClickIds(page.Render());
        await page.InvokeAsync(ids[1]); // select "a" (count 1 < 2)
        Assert.Contains("Pick at least two.", page.Render());

        var ids2 = ClickIds(page.Render());
        await page.InvokeAsync(ids2[3]); // select "b" (count 2)
        Assert.DoesNotContain("Pick at least two.", page.Render());
    }

    [Fact]
    public async Task AfterBind_FiresWithMutatedCollection()
    {
        var model = new Bag();
        ICollection<string>? seen = null;
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, Options, AfterBind: c => seen = c)],
            TestServices.Default());

        var ids = ClickIds(page.Render());
        await page.InvokeAsync(ids[2]); // select "b"

        Assert.NotNull(seen);
        Assert.Equal(["b"], seen!);
        Assert.Same(model.Tags, seen); // bound mode mutates the model collection in place
    }

    [Fact]
    public async Task AfterBindAsync_Fires()
    {
        var model = new Bag();
        var fired = false;
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(
                () => model.Tags, Options, AfterBindAsync: _ =>
                {
                    fired = true;
                    return Task.CompletedTask;
                })],
            TestServices.Default());

        await page.InvokeAsync(ClickIds(page.Render())[1]);

        Assert.True(fired);
    }

    [Fact]
    public async Task Controlled_OnChange_EmitsNewSelection_WithoutMutatingValue()
    {
        var value = new List<string> { "a" };
        ICollection<string>? emitted = null;
        var page = RaskTest.Render(
            () => BsMultiSelect<string>(Options, Value: value, OnChange: next => emitted = next),
            TestServices.Default());

        var ids = ClickIds(page.Render()); // [box, chip-remove-a, opt-a, opt-b, opt-c]
        await page.InvokeAsync(ids[3]); // select "b"

        Assert.NotNull(emitted);
        Assert.Equal(["a", "b"], emitted!);
        Assert.Equal(["a"], value); // controlled mode never mutates the parent's Value in place
    }

    [Fact]
    public async Task Controlled_OnChangeAsync_Fires()
    {
        var value = new List<string>();
        ICollection<string>? emitted = null;
        var page = RaskTest.Render(
            () => BsMultiSelect<string>(Options, Value: value, OnChangeAsync: next =>
            {
                emitted = next;
                return Task.CompletedTask;
            }),
            TestServices.Default());

        await page.InvokeAsync(ClickIds(page.Render())[1]); // select "a"

        Assert.Equal(["a"], emitted!);
    }

    [Fact]
    public void Controlled_NoValidationMessage_NoEditContext()
    {
        var page = RaskTest.Render(
            () => BsMultiSelect<string>(Options, Value: new List<string>(), OnChange: _ => { }),
            TestServices.Default());

        var html = page.Render();
        Assert.Contains("dropdown-item", html);
        Assert.DoesNotContain("invalid-feedback", html); // no bound field → no ValidationMessage
    }

    [Fact]
    public void Disabled_ToggleBoxHasNoClickHandler()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, Options, Disabled: true)],
            TestServices.Default());

        var html = page.Render();

        Assert.Contains("form-select", html);
        Assert.Contains("disabled", html);
        Assert.DoesNotContain("data-rask-on-click", html); // inert: no toggle or option handlers wired
    }

    [Fact]
    public void NullOptions_ThrowsArgumentNullException()
    {
        var model = new Bag();
        // Render() renders as it is called, so the render-time throw surfaces from the call itself.
        Assert.Throws<ArgumentNullException>(() => RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, null!)],
            TestServices.Default()));
    }

    [Fact]
    public void NeitherBindNorValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RaskTest.Render(
            () => BsMultiSelect<string>(Options),
            TestServices.Default()));
    }

    [Fact]
    public async Task OptionDisabled_NotToggleableByClick_AndSkippedByKeyboard()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, Options, OptionDisabled: o => o == "b")],
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open, cursor seeded to option 0 ("a")

        var html = page.Render();
        Assert.Contains("aria-disabled=\"true\"", html);       // "b" is disabled
        // the disabled "b" has no click handler: box + opt-a + opt-c + backdrop = 4 (would be 5 if b enabled)
        Assert.Equal(4, ClickIds(html).Count);

        // ArrowDown from "a" (0) skips the disabled "b" (1) and lands on "c" (2)
        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"ArrowDown\"}");
        Assert.Equal("Tags-opt-2", Markup.Attr(page.Render(), "aria-activedescendant")!);
    }

    [Fact]
    public async Task SelectAll_AddsAllEnabled_ThenClearsAll()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(() => model.Tags, Options, SelectAll: true)],
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        // Empty selection → no chips, so the header is the first click handler after the box.
        await page.InvokeAsync(ClickIds(page.Render())[1]); // "Select all"
        Assert.Equal(["a", "b", "c"], model.Tags);
        Assert.Contains("Clear all", page.Render());

        // Now chips render before the menu rows; the header sits after box + one remove-button per chip.
        await page.InvokeAsync(ClickIds(page.Render())[1 + model.Tags.Count]); // "Clear all"
        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task SelectAll_ExcludesDisabledOptions()
    {
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(
                () => model.Tags, Options, SelectAll: true, OptionDisabled: o => o == "b")],
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        await page.InvokeAsync(ClickIds(page.Render())[1]); // "Select all" — adds only the enabled options
        Assert.Equal(["a", "c"], model.Tags);               // "b" is disabled, never added
    }

    [Fact]
    public async Task SelectAll_Controlled_EmitsFullSelection_WithoutMutatingValue()
    {
        var value = new List<string>();
        ICollection<string>? emitted = null;
        var page = RaskTest.Render(
            () => BsMultiSelect<string>(Options, Value: value, SelectAll: true, OnChange: next => emitted = next),
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open

        await page.InvokeAsync(ClickIds(page.Render())[1]); // "Select all"

        Assert.Equal(["a", "b", "c"], emitted!);
        Assert.Empty(value); // controlled mode never mutates the parent's Value in place
    }

    [Fact]
    public async Task Grouped_ArrowNavigation_WalksFlatOrder_SkippingHeadersAndDisabled()
    {
        // Options a,b,c,d grouped a,c -> "Odd", b,d -> "Even" (first-seen: Odd then Even), so the flat cursor
        // order is a(0), c(1), b(2), d(3). Disable "c" (flat 1).
        var model = new Bag();
        var page = RaskTest.Render(
            () => Form(model)[BsMultiSelect<string>(
                () => model.Tags, ["a", "b", "c", "d"],
                OptionGroup: o => o is "a" or "c" ? "Odd" : "Even",
                OptionDisabled: o => o == "c")],
            TestServices.Default());
        await page.InvokeAsync(ClickIds(page.Render())[0]); // open, cursor seeds to flat 0 ("a")

        var html = page.Render();
        Assert.Contains("dropdown-header", html);                                  // grouped headers render
        Assert.Equal("Tags-opt-0", Markup.Attr(html, "aria-activedescendant")!);   // a (flat 0)

        // ArrowDown from a(0): the next flat option c(1) is disabled → skip it and the headers, land on b(2).
        await page.InvokeAsync(page.HandlerIds("keydown")[0], "{\"key\":\"ArrowDown\"}");
        Assert.Equal("Tags-opt-2", Markup.Attr(page.Render(), "aria-activedescendant")!);
    }

    private static IReadOnlyList<string> ClickIds(string html) =>
        Markup.Attrs(html, "data-rask-on-click");

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }

    private sealed class Bag
    {
        public List<string> Tags { get; } = [];
    }
}
