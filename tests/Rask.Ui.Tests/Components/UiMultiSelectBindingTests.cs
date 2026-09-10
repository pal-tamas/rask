using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The multi-select binds the collection your model already declares, and writes back into the shape
///     it declared it in.
/// </summary>
/// <remarks>
///     <para>
///         The chain opens on <c>ICollection&lt;T&gt;</c>, which is what lets a <c>List&lt;T&gt;</c>, a
///         <c>T[]</c> and a <c>HashSet&lt;T&gt;</c> all infer the same element type from one step. The
///         write-back is the half that cannot be seen in markup, so it is exercised directly.
///     </para>
///     <para>
///         Every case here is a shape a real model actually declares. The one that is NOT supported is
///         <c>IReadOnlyList&lt;T&gt;</c> — it is not an <c>ICollection&lt;T&gt;</c>, so the chain has
///         nothing to infer from and the call site does not compile.
///     </para>
/// </remarks>
public partial class UiMultiSelectBindingTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Value, string Text)[] Packages =
        [("core", "Rask.Core"), ("ui", "Rask.Ui"), ("cli", "Rask.Cli")];

    private static readonly (int Value, string Text)[] Numbers =
        [(1, "One"), (2, "Two"), (3, "Three")];

    [Fact]
    public void A_bound_list_draws_the_model()
    {
        // The assertion that matters: the MODEL, not a separate Value, is what the markup draws.
        var model = new Bag { Tags = ["core", "cli"] };
        var html = UiMultiSelect.Bind(() => model.Tags).Options(Packages).Label("Packages")
            .Native(false).ToHtml();

        Assert.Contains("Rask.Core", html);
        Assert.Contains("Rask.Cli", html);
        Assert.Equal(2, Occurrences(html, "aria-selected=\"true\""));
    }

    [Fact]
    public void The_element_type_comes_off_the_collection_not_off_string()
    {
        // The framework's own Select<T> can only bind a selection whose elements are strings, for a
        // documented AOT reason. This control maps the picked values through its own option list
        // instead, so an int — or an enum, or a Guid — binds exactly as well.
        var model = new Bag { Ids = [2] };
        var html = UiMultiSelect.Bind(() => model.Ids).Options(Numbers).Label("Numbers")
            .Native(false).ToHtml();

        Assert.Contains("aria-selected=\"true\"", html);
        Assert.Contains("Two", html);
    }

    [Fact]
    public async Task A_settable_list_is_replaced()
    {
        var model = new Bag { Tags = ["core"] };

        await CommitAsync(() => model.Tags, ["ui", "cli"]);

        Assert.Equal(["ui", "cli"], model.Tags);
    }

    [Fact]
    public async Task A_settable_array_gets_an_array()
    {
        // Handing a List<string> to a string[] property throws, and nothing in the value's own type says
        // which one the property wants — so the write-back reads the DECLARED type and builds that.
        var model = new Bag { Roles = ["admin"] };

        await CommitAsync(() => model.Roles, ["editor", "reader"]);

        Assert.Equal(["editor", "reader"], model.Roles);
    }

    [Fact]
    public async Task A_settable_set_gets_a_set()
    {
        var model = new Bag();

        await CommitAsync(() => model.Ids, [1, 3]);

        Assert.Equal([1, 3], model.Ids.Order());
    }

    [Fact]
    public async Task A_get_only_collection_is_refilled_in_place()
    {
        // `public List<string> Notes { get; } = [];` is an ordinary way to declare one of these, and it
        // has no setter at all — so the collection the model already owns is emptied and refilled rather
        // than replaced. Checked before the settable path, because calling a missing setter throws where
        // assigning over a settable property would have been fine either way.
        var model = new Bag();
        model.Notes.Add("stale");
        var same = model.Notes;

        await CommitAsync(() => model.Notes, ["fresh"]);

        Assert.Equal(["fresh"], model.Notes);
        Assert.Same(same, model.Notes);
    }

    [Fact]
    public async Task Committing_an_empty_selection_clears_the_field()
    {
        // "Nothing picked" has to be writable. A control that could only ever add is one a user cannot
        // undo without reloading the page.
        var model = new Bag { Tags = ["core", "ui"] };

        await CommitAsync(() => model.Tags, []);

        Assert.Empty(model.Tags);
    }

    [Fact]
    public async Task A_controlled_change_hands_over_a_fresh_collection()
    {
        // Never the collection the parent handed down: mutating that one changes the parent's state
        // behind its back and leaves OnChange looking like a no-op, because the "new" value and the old
        // are the same object.
        var held = new List<string> { "core" };
        ICollection<string>? got = null;
        var control = new UiMultiSelect<string>
        {
            Label = "Packages",
            Options = Packages,
            Value = held,
            OnChange = v => got = v
        };

        await UiFormCommit.CommitSelectionAsync(control, null, null, ["ui"]);

        Assert.Equal(["ui"], got);
        Assert.NotSame(held, got);
        Assert.Equal(["core"], held);
    }

    [Fact]
    public async Task A_value_the_option_list_does_not_carry_survives_a_pick()
    {
        // The data-loss case, driven through the CONTROL rather than the write-back helper — the helper
        // writes what it is handed, and the composing is the part under test. A bound field may hold a
        // value with no option: a list narrowed by permissions, a tag retired since the row was saved.
        // It has no row and no chip, so the user cannot see it and cannot have meant to remove it, and
        // rebuilding the commit from only the visible answers would delete it as a side effect of
        // touching a different one.
        var model = new Bag { Tags = ["core", "legacy"] };
        var page = global::Rask.Testing.RaskTest.Render(
            UiMultiSelect.Bind(() => model.Tags).Options(Packages).Label("Packages").Native(false));

        await page.On("[role=\"option\"]:has-text(\"Rask.Ui\")").ClickAsync();

        Assert.Equal(["core", "ui", "legacy"], model.Tags);
    }

    [Fact]
    public async Task Removing_the_last_visible_answer_still_keeps_the_unseen_one()
    {
        // The same rule from the other end: emptying what the box shows is not a licence to empty what
        // it does not. The control never adds or removes a value it cannot draw.
        var model = new Bag { Tags = ["core", "legacy"] };
        var page = global::Rask.Testing.RaskTest.Render(
            UiMultiSelect.Bind(() => model.Tags).Options(Packages).Label("Packages").Native(false));

        await page.On("[aria-label=\"Remove Rask.Core\"]").ClickAsync();

        Assert.Equal(["legacy"], model.Tags);
    }

    [Fact]
    public void A_value_the_option_list_does_not_carry_is_not_drawn()
    {
        // Carried, but never shown: it has no option to mark and no words to put in a chip.
        var model = new Bag { Tags = ["core", "legacy"] };
        var html = UiMultiSelect.Bind(() => model.Tags).Options(Packages).Label("Packages")
            .Native(false).ToHtml();

        Assert.DoesNotContain("legacy", html);
        Assert.Equal(1, Occurrences(html, "aria-selected=\"true\""));
    }

    [Fact]
    public void A_value_the_option_list_does_not_carry_still_posts()
    {
        // It is part of the field's value, and a plain form has no model to carry it separately — so
        // leaving it out of the hidden inputs would drop on submit exactly what the commit path takes
        // care to keep. The same loss, arriving by the other road.
        var model = new Bag { Tags = ["core", "legacy"] };
        var html = UiMultiSelect.Bind(() => model.Tags).Options(Packages).Label("Packages")
            .Native(false).Name("tags").ToHtml();

        Assert.Equal(2, Occurrences(html, "type=\"hidden\""));
        Assert.Contains("value=\"legacy\"", html);
    }

    private static Task CommitAsync<T>(Expression<Func<ICollection<T>>> bind, IReadOnlyList<T> picked)
    {
        var control = new UiMultiSelect<T> { Label = "Field", Options = [] };
        return UiFormCommit.CommitSelectionAsync(control, ExpressionAccessor.Parse(bind), null, picked);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class Bag
    {
        public List<string> Tags { get; set; } = [];

        public string[] Roles { get; set; } = [];

        public HashSet<int> Ids { get; set; } = [];

        public List<string> Notes { get; } = [];
    }
}
