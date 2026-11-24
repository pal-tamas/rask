using System.Linq.Expressions;
using Rask.Cqrs;

namespace Rask.Data.Tests;

public sealed class RefuseTests
{
    [Fact]
    public void A_condition_that_holds_with_no_field_refuses_the_whole_form()
    {
        var refused = Assert.Throws<RaskValidationException>(() => Refuse.When(true, "No place to write from."));

        var failure = Assert.Single(refused.Failures);
        Assert.Empty(failure.Fields);
        Assert.Equal("No place to write from.", failure.Message);
        Assert.Equal("No place to write from.", refused.Message);
    }

    [Fact]
    public void A_condition_that_does_not_hold_refuses_nothing_and_never_reads_the_lambda()
    {
        var form = new FuelForm();

        Refuse.When(false, "Never said.");
        Refuse.When(false, () => form.Total(), "A method call is no field, but it is never read here.");
        Refuse.Unless(true, () => form.Total(), "Nor here.");

        Assert.Equal(0, form.Totalled);
    }

    [Fact]
    public void A_captured_value_and_its_member_name_the_field_by_its_path()
    {
        var km = new KmRange(10, 5);

        var refused = Assert.Throws<RaskValidationException>(() => Refuse.When(km.Start > km.End, () => km.Start, "Start after end."));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["Km.Start"], failure.Fields);
        Assert.Equal("Start after end.", failure.Message);
    }

    [Fact]
    public void A_field_read_through_this_leaves_this_out()
    {
        var form = new FuelForm();

        var refused = Assert.Throws<RaskValidationException>(() => form.RefuseStart());

        Assert.Equal(["Km.Start"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void An_element_of_a_list_is_named_by_its_evaluated_index()
    {
        var form = new FuelForm();
        var i = 2;

        var refused = Assert.Throws<RaskValidationException>(() => form.RefusePart(i));

        Assert.Equal(["Parts[2].Quantity"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void A_captured_local_is_named_as_its_form_field()
    {
        List<Part> parts = [new(1), new(2), new(3)];
        var i = 1;

        var refused = Assert.Throws<RaskValidationException>(() => Refuse.Unless(false, () => parts[i].Quantity, "Locked."));

        Assert.Equal(["Parts[1].Quantity"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void An_array_element_and_an_index_computed_in_place_are_both_evaluated()
    {
        Part[] parts = [new(1), new(2), new(3)];
        var i = 0;

        var refused = Assert.Throws<RaskValidationException>(() => Refuse.At(() => parts[i + 2].Quantity, "Too much."));

        Assert.Equal(["Parts[2].Quantity"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void A_dictionary_key_is_named_as_written()
    {
        var limits = new Dictionary<string, Part>(StringComparer.Ordinal) { ["k1"] = new(5) };

        var refused = Assert.Throws<RaskValidationException>(() => Refuse.At(() => limits["k1"].Quantity, "Over the limit."));

        Assert.Equal(["Limits[k1].Quantity"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void A_lambda_that_is_not_a_member_chain_is_refused_with_the_shape_to_write()
    {
        var form = new FuelForm();
        Expression<Func<int>> total = () => form.Total();

        var wrong = Assert.Throws<ArgumentException>(() => Refuse.At(total, "Never said."));

        Assert.Contains("() => Parts[i].Quantity", wrong.Message, StringComparison.Ordinal);
    }

    private sealed record Part(int Quantity);

    private sealed record KmRange(int Start, int End);

    private sealed class FuelForm
    {
        public KmRange Km { get; } = new(10, 5);

        public List<Part> Parts { get; } = [new(1), new(2), new(3)];

        public int Totalled { get; private set; }

        public int Total() => ++Totalled;

        public void RefuseStart() => Refuse.When(Km.Start > Km.End, () => this.Km.Start, "Start after end.");

        public void RefusePart(int index) => Refuse.At(() => Parts[index].Quantity, "Locked.");
    }
}
