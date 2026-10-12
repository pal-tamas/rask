using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

/// <summary>
///     A control that draws itself in C# hands what was chosen to the session: its own event draws the pick, and
///     the write, the rules and <c>AfterBind</c> go ahead of the next handler that is not such a control's own.
/// </summary>
public partial class PendingBindsTests : global::Rask.Core.RaskMarkup
{
    private sealed class Order
    {
        public string Size { get; set; } = "";

        public string Colour { get; set; } = "";
    }

    private static IEnumerable<string> NotLarge(string size) => size == "large" ? ["Too large."] : [];

    [Fact]
    public async Task A_pick_is_drawn_at_once_and_written_with_the_next_handler_of_the_page()
    {
        var m = new Order();
        string? sizeWhenSaved = null;
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size"),
            Button.Id("save").OnClick(() => sizeWhenSaved = m.Size)["Save"]
        ]);

        await page.On("#size .small").Click();
        var (drawn, written) = (page.TextOf("#size .shown"), m.Size);
        await page.On("#save").Click();

        Assert.Equal("small", drawn);
        Assert.Equal("", written);
        Assert.Equal("small", sizeWhenSaved);
    }

    [Fact]
    public async Task What_waits_is_written_in_the_order_the_controls_were_last_touched()
    {
        var m = new Order();
        List<string> order = [];
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size").AfterBind(v => order.Add("size " + v)),
            WaitingPicker.Bind(() => m.Colour).Name("colour").AfterBind(v => order.Add("colour " + v)),
            Button.Id("save").OnClick(() => order.Add("save"))["Save"]
        ]);

        await page.On("#size .small").Click();
        await page.On("#colour .small").Click();
        await page.On("#size .large").Click();
        var before = order.Count;
        await page.On("#save").Click();

        Assert.Equal(0, before);
        Assert.Equal(["colour small", "size large", "save"], order);
    }

    [Fact]
    public async Task Every_value_is_written_before_the_first_rule_or_AfterBind_runs()
    {
        var m = new Order();
        string? colourWhenSizeArrived = null;
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size").AfterBind(_ => colourWhenSizeArrived = m.Colour),
            WaitingPicker.Bind(() => m.Colour).Name("colour"),
            Button.Id("save").OnClick(static () => { })["Save"]
        ]);
        await page.On("#size .small").Click();
        await page.On("#colour .large").Click();

        await page.On("#save").Click();

        Assert.Equal("large", colourWhenSizeArrived);
    }

    [Fact]
    public async Task A_live_control_writes_as_it_is_chosen_after_what_waited()
    {
        var m = new Order();
        string? sizeWhenColourArrived = null;
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size"),
            WaitingPicker.Bind(() => m.Colour).Name("colour").Live().AfterBind(_ => sizeWhenColourArrived = m.Size)
        ]);
        await page.On("#size .small").Click();

        await page.On("#colour .large").Click();

        Assert.Equal("large", m.Colour);
        Assert.Equal("small", sizeWhenColourArrived);
    }

    [Fact]
    public async Task A_rule_speaks_when_the_value_is_written_and_its_message_goes_at_the_next_pick()
    {
        var m = new Order();
        var page = Page.Render(() => Form.Model(m)[
            WaitingPicker.Bind(() => m.Size).Name("size").Validate(NotLarge),
            Validation.Message.Template(messages => P[string.Join(" ", messages)]).For(() => m.Size),
            Button.Id("save").Type(ButtonType.Button).OnClick(static () => { })["Save"]
        ]);

        await page.On("#size .large").Click();
        var atThePick = page.Html;
        await page.On("#save").Click();
        var atTheAction = page.Html;
        await page.On("#size .small").Click();

        Assert.DoesNotContain("Too large.", atThePick, StringComparison.Ordinal);
        Assert.Contains("Too large.", atTheAction, StringComparison.Ordinal);
        Assert.DoesNotContain("Too large.", page.Html, StringComparison.Ordinal);
        Assert.Equal("large", m.Size);
    }

    [Fact]
    public async Task A_pick_marks_the_field_modified_before_the_model_hears_it()
    {
        var m = new Order();
        var form = new EditContext(m);
        var page = Page.Render(() => Form.Model(m).Context(form)[WaitingPicker.Bind(() => m.Size).Name("size")]);

        await page.On("#size .small").Click();

        Assert.True(form.IsModified(new FieldIdentifier(m, nameof(Order.Size))));
        Assert.Equal("", m.Size);
    }

    [Fact]
    public async Task A_pick_survives_a_render_the_page_starts_and_is_still_written_with_the_action()
    {
        var m = new Order();
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size"),
            Button.Id("save").OnClick(static () => { })["Save"]
        ]);
        await page.On("#size .small").Click();

        page.Render();
        var drawn = page.TextOf("#size .shown");
        var written = m.Size;
        await page.On("#save").Click();

        Assert.Equal("small", drawn);
        Assert.Equal("", written);
        Assert.Equal("small", m.Size);
    }

    [Fact]
    public async Task A_pick_in_a_control_a_render_removed_is_dropped_and_one_removed_by_the_action_is_written()
    {
        var (dropped, kept) = (new Order(), new Order());
        var (showDropped, showKept) = (true, true);
        var page = Page.Render(() => Fragment[
            Div[showDropped ? WaitingPicker.Bind(() => dropped.Size).Name("dropped") : null],
            Div[showKept ? WaitingPicker.Bind(() => kept.Size).Name("kept") : null],
            Button.Id("save").OnClick(() => showKept = false)["Save"]
        ]);
        await page.On("#dropped .small").Click();
        await page.On("#kept .small").Click();

        showDropped = false;
        page.Render();
        await page.On("#save").Click();

        Assert.Equal("", dropped.Size);
        Assert.Equal("small", kept.Size);
        Assert.False(page.Exists("#kept"));
    }

    [Fact]
    public async Task A_controls_own_handlers_do_not_write_what_another_control_holds()
    {
        var m = new Order();
        var page = Page.Render(() => Fragment[
            WaitingPicker.Bind(() => m.Size).Name("size"),
            WaitingPicker.Bind(() => m.Colour).Name("colour")
        ]);

        await page.On("#size .small").Click();
        await page.On("#colour .large").Click();
        await page.On("#colour .small").Click();

        Assert.Equal(("", ""), (m.Size, m.Colour));
        Assert.Equal("small", page.TextOf("#size .shown"));
    }
}

/// <summary>A control of two buttons that commits what was pressed the way the kit's drawn controls do.</summary>
public sealed partial class WaitingPicker : Component, IDeferredBind
{
    /// <summary>The bound member.</summary>
    public Expression<Func<string>>? Bind { get; set; }

    /// <summary>Writes as it is chosen.</summary>
    public bool? Live { get; set; }

    /// <summary>The member's rule.</summary>
    public Validator<string>? Validate { get; set; }

    /// <summary>Runs after the write.</summary>
    public Callback<string> AfterBind { get; set; }

    /// <summary>The id of the control's box.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var bound = ExpressionAccessor.Parse(Bind!);
        var form = BindingHelpers.ResolveBindingContext(bound.Target);
        form?.RegisterFieldValidator(bound.Field, Validate?.Rule, () => bound.Getter());
        var shown = PendingBinds.TryPeek(this, bound.Field, out var chosen) ? chosen : bound.Getter();

        return Div.Id(Name)[
            Span.Class("shown")[shown as string],
            Button.Class("small").Type(ButtonType.Button).OnClick(() => Pick(bound, form, "small"))["small"],
            Button.Class("large").Type(ButtonType.Button).OnClick(() => Pick(bound, form, "large"))["large"]
        ];
    }

    private Task Pick(ExpressionAccessor.Accessor bound, EditContext? form, string value)
    {
        return PendingBinds.Commit(this, bound.Field, form, value, Write, Settle);

        bool Write()
        {
            bound.Setter(value);
            return true;
        }

        async Task Settle()
        {
            await BindingHelpers.NotifyAndValidateField(form, bound.Field);
            await AfterBind.Invoke(value);
        }
    }
}
