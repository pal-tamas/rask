using Rask.Core.Forms;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The kit's value-type controls binding a NULLABLE property — the shape every property of a generated
///     form model has, so <c>UiCheckbox.Bind(() =&gt; model.InStock)</c> over a <c>bool?</c> has to compile,
///     draw and write back as surely as over a <c>bool</c>.
/// </summary>
/// <remarks>
///     The controls stay over <c>bool</c>, <c>int</c>, <c>double</c> and <c>DateOnly</c>, so their controlled
///     half keeps a plain value. The chain adds a second <c>Bind</c> opening taking <c>Expression&lt;Func&lt;T?&gt;&gt;</c>,
///     which <see cref="ExpressionAccessor.NonNullable{T}" /> turns into the control's own bind.
/// </remarks>
public partial class UiNullableBindingTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void A_checkbox_bound_to_a_nullable_bool_is_checked_only_by_true(bool? inStock, bool isChecked)
    {
        var model = new Form { InStock = inStock };

        var html = UiCheckbox.Bind(() => model.InStock).Text("In stock").ToHtml();

        Assert.Equal(isChecked, html.Contains("checked", StringComparison.Ordinal));
    }

    [Fact]
    public void A_toggle_and_a_radio_bind_a_nullable_bool_too()
    {
        var model = new Form { InStock = true };

        Assert.Contains("checked", UiToggle.Bind(() => model.InStock).Text("In stock").ToHtml());
        Assert.Contains("checked", UiRadio.Bind(() => model.InStock).Text("In stock").Group("stock").ToHtml());
    }

    [Fact]
    public void A_range_bound_to_a_nullable_double_draws_the_model_and_nothing_for_null()
    {
        var set = new Form { Volume = 40 };
        var unset = new Form();

        Assert.Contains("value=\"40\"", UiRange.Bind(() => set.Volume).Label("Volume").ToHtml());
        Assert.DoesNotContain("value=\"40\"", UiRange.Bind(() => unset.Volume).Label("Volume").ToHtml());
    }

    [Fact]
    public void A_rating_bound_to_a_nullable_int_reads_null_as_unrated()
    {
        var model = new Form();

        var html = UiRating.Bind(() => model.Stars).Group("score").Label("Rate this").Max(5).ToHtml();

        Assert.Contains("value=\"0\" checked", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_calendar_bound_to_a_nullable_date_marks_its_day_and_nothing_for_null()
    {
        var set = new Form { Due = new DateOnly(2026, 3, 14) };
        var unset = new Form();

        Assert.Contains("aria-pressed=\"true\"",
            UiCalendar.Bind(() => set.Due).Label("Due").Month(March).ToHtml());
        Assert.DoesNotContain("aria-pressed=\"true\"",
            UiCalendar.Bind(() => unset.Due).Label("Due").Month(March).ToHtml());
    }

    [Fact]
    public async Task A_commit_through_the_lifted_bind_writes_the_nullable_property()
    {
        var model = new Form();
        var control = new UiRating { Group = "score", Label = "Rate this", Bind = ExpressionAccessor.NonNullable(() => model.Stars) };

        var (acc, ctx, current) = UiFormCommit.Resolve<int>(control);
        Assert.Equal(0, current);

        await UiFormCommit.CommitAsync(control, acc, ctx, 4);

        Assert.Equal(4, model.Stars);
    }

    [Fact]
    public void The_lifted_bind_names_and_writes_the_property_itself()
    {
        // The Convert it adds is stripped by Parse, so the field the form validates is `InStock` and a
        // bool written through it lands in the bool? — nothing ever evaluates the conversion on a null.
        var model = new Form();
        var accessor = ExpressionAccessor.Parse(ExpressionAccessor.NonNullable(() => model.InStock));

        Assert.Equal("InStock", accessor.PropertyName);
        Assert.Null(accessor.Getter());

        accessor.Setter(true);

        Assert.True(model.InStock);
    }

    [Fact]
    public void A_plain_bool_still_takes_the_exact_overload()
    {
        // Both openings accept `() => model.Agreed` over a bool; C# prefers the identity return conversion,
        // so no Convert is added and the bind is exactly what was written.
        var model = new Plain { Agreed = true };

        var control = UiCheckbox.Bind(() => model.Agreed).Text("I agree");

        Assert.IsAssignableFrom<System.Linq.Expressions.MemberExpression>(control.Bind!.Body);
    }

    private sealed class Form
    {
        public bool? InStock { get; set; }

        public double? Volume { get; set; }

        public int? Stars { get; set; }

        public DateOnly? Due { get; set; }
    }

    private sealed class Plain
    {
        public bool Agreed { get; set; }
    }
}
