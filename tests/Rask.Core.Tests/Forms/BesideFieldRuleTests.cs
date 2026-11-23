using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A type-carried rule that reads the fields beside its own — a range's end against its start — is registered as a
// Func<object, T, IEnumerable<string>> and handed the object that holds the field, so it runs live like any other.
public partial class BesideFieldRuleTests : global::Rask.Core.RaskMarkup
{
    static BesideFieldRuleTests() =>
        RaskValidation.RegisterFieldRules(typeof(Span), static name => name == nameof(Span.End) ? EndRule : null);

    private static readonly Func<object, int?, IEnumerable<string>> EndRule =
        static (holder, end) => holder is Span { Start: { } start } && end <= start ? [$"It must be after the start, {start}."] : [];

    [Fact]
    public async Task An_end_is_checked_against_the_start_beside_it_as_it_is_committed()
    {
        var span = new Span { Start = 10 };
        EditContext? form = null;
        var page = Page.Render(() => Form.Model(span)[
            Input.Bind(() => span.End).Id("end").Blur(), Test.EditContextProbe(c => form = c)]);

        await page.On("#end").Change("5");
        var refused = Messages(form!, span);
        await page.On("#end").Change("12");

        Assert.Equal(["It must be after the start, 10."], refused);
        Assert.Empty(Messages(form!, span));
    }

    [Fact]
    public void A_rule_beside_its_field_is_made_once_per_holder()
    {
        var span = new Span();
        var field = new FieldIdentifier(span, nameof(Span.End));

        var first = RaskValidation.RuleFor<int?>(field, null);
        var again = RaskValidation.RuleFor<int?>(field, null);
        var otherType = RaskValidation.RuleFor<long?>(field, null);

        Assert.IsType<Validate<int?>>(first);
        Assert.Same(first, again);
        Assert.Null(otherType);
    }

    private static IReadOnlyList<string> Messages(EditContext form, Span span) =>
        [.. form.GetValidationMessages(new FieldIdentifier(span, nameof(Span.End)))];

    private sealed class Span
    {
        public int? Start { get; set; }

        public int? End { get; set; }
    }
}
