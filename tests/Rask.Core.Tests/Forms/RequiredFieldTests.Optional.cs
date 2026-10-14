using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// A part of a model that may be left wholly empty: nothing under it has to be filled, and the rule its fields'
// type carries is asked only about a value that was filled in. It is said of the PROPERTY that holds the part,
// so one type held both ways — an address that must be there and one that may be absent — answers per place.
public partial class RequiredFieldTests
{
    private static void RegisterTheOrder()
    {
        RaskValidation.RegisterRequiredFields(typeof(Address), Address.IsRequired);
        RaskValidation.RegisterRequiredFields(typeof(Door), Door.IsRequired);
        RaskValidation.RegisterFieldRules(typeof(Address), Address.RuleOf);
        RaskValidation.RegisterOptionalFields(typeof(Order), Order.IsOptional);
        RaskValidation.RegisterOptionalFields(typeof(Address), Address.IsOptional);
    }

    [Fact]
    public void A_field_is_required_where_its_object_must_be_there_and_not_where_the_same_type_may_be_absent()
    {
        var model = typeof(Order);

        var mustBeThere = RaskValidation.IsRequired(model, "Shipping.Street");
        var mayBeAbsent = RaskValidation.IsRequired(model, "Billing.Street");
        var asked = RaskValidation.IsRequired(typeof(Address), nameof(Address.Street));

        Assert.True(mustBeThere);
        Assert.False(mayBeAbsent);
        Assert.True(asked);
    }

    [Fact]
    public void Nothing_under_an_object_that_may_be_absent_is_required_however_deep_or_through_a_row()
    {
        var model = typeof(Order);

        var twoDownWhereItMustBeThere = RaskValidation.IsRequired(model, "Shipping.Front.Number");
        var twoDownWhereItMayBeAbsent = RaskValidation.IsRequired(model, "Billing.Front.Number");
        var optionalInsideWhatMustBeThere = RaskValidation.IsRequired(model, "Shipping.Back.Number");
        var throughARow = RaskValidation.IsRequired(model, "Stops[1].Back.Number");
        var attributedUnderOne = RaskValidation.IsRequired(model, "Billing.City");

        Assert.True(twoDownWhereItMustBeThere);
        Assert.False(twoDownWhereItMayBeAbsent);
        Assert.False(optionalInsideWhatMustBeThere);
        Assert.False(throughARow);
        Assert.False(attributedUnderOne);
    }

    [Fact]
    public void A_bind_expression_answers_for_the_place_it_walks_through()
    {
        var order = new Order();

        var mustBeThere = RaskValidation.IsRequired(() => order.Shipping.Street);
        var mayBeAbsent = RaskValidation.IsRequired(() => order.Billing!.Street);
        var twoDown = (RaskValidation.IsRequired(() => order.Shipping.Front.Number), RaskValidation.IsRequired(() => order.Billing!.Front.Number));
        var throughARow = (RaskValidation.IsRequired(() => order.Stops[1].Street), RaskValidation.IsRequired(() => order.Stops[1].Back!.Number));
        var unregistered = RaskValidation.IsRequired(() => order.Note);
        var noMember = RaskValidation.IsRequired(() => 1);
        var noExpression = Record.Exception(() => RaskValidation.IsRequired((System.Linq.Expressions.LambdaExpression)null!));

        Assert.True(mustBeThere);
        Assert.False(mayBeAbsent);
        Assert.Equal((true, false), twoDown);
        Assert.Equal((true, false), throughARow);
        Assert.False(unregistered);
        Assert.False(noMember);
        Assert.IsType<ArgumentNullException>(noExpression);
    }

    [Fact]
    public void A_bound_input_says_aria_required_where_the_object_must_be_there_and_nothing_where_it_may_be_absent()
    {
        var order = new Order { Billing = new Address() };

        var html = Page.Render(() => Div[
            Input.Bind(() => order.Shipping.Street).Id("ship"),
            Input.Bind(() => order.Billing!.Street).Id("bill"),
            Textarea.Bind(() => order.Billing!.Street).Id("bill-text"),
            Select.Bind(() => order.Billing!.Street).Id("bill-pick")[Option.Value("a")["A"]]
        ]).Render();

        Assert.Contains("""<input id="ship" aria-required="true" """, html, StringComparison.Ordinal);
        Assert.Equal(1, Announced().Count(html));
    }

    [Fact]
    public void The_rule_a_fields_type_carries_is_not_asked_about_an_unfilled_value_under_an_object_that_may_be_absent()
    {
        var order = new Order { Billing = new Address() };
        var shipping = ExpressionAccessor.Parse(() => order.Shipping.Street);
        var billing = ExpressionAccessor.Parse(() => order.Billing!.Street);

        var strict = (Validate<string>)RaskValidation.RuleFor<string>(shipping.Field, null, shipping.UnderOptional)!;
        var relaxed = (Validate<string>)RaskValidation.RuleFor<string>(billing.Field, null, billing.UnderOptional)!;

        Assert.Equal((false, true), (shipping.UnderOptional, billing.UnderOptional));
        Assert.Equal([Address.Unfilled], strict(""));
        Assert.Empty(relaxed(""));
        Assert.Empty(relaxed("   "));
        Assert.Equal([Address.TooShort], relaxed("x"));
        Assert.Empty(relaxed("1 Quay"));
    }

    [Fact]
    public void The_relaxed_rule_is_one_kept_delegate_and_an_unticked_box_counts_as_unfilled()
    {
        var order = new Order { Billing = new Address() };
        var street = ExpressionAccessor.Parse(() => order.Billing!.Street);
        var lit = ExpressionAccessor.Parse(() => order.Billing!.Lit);

        var first = RaskValidation.RuleFor<string>(street.Field, null, underOptional: true);
        var again = RaskValidation.RuleFor<string>(street.Field, null, underOptional: true);
        var box = (Validate<bool?>)RaskValidation.RuleFor<bool?>(lit.Field, null, underOptional: true)!;

        Assert.Same(first, again);
        Assert.Empty(box(null));
        Assert.Empty(box(false));
        Assert.Equal([Address.Ticked], box(true));
    }

    private sealed class Order
    {
        public Address Shipping { get; set; } = new();

        public Address? Billing { get; set; }

        public List<Address> Stops { get; set; } = [new(), new()];

        public string Note { get; set; } = "";

        internal static bool IsOptional(string property) => property == nameof(Billing);
    }

    private sealed class Address
    {
        internal const string Unfilled = "Say the street.";
        internal const string TooShort = "A street has more than one letter.";
        internal const string Ticked = "Asked about a ticked box.";

        private static readonly Validate<string> StreetRule = value =>
            string.IsNullOrWhiteSpace(value) ? [Unfilled] : value.Length < 2 ? [TooShort] : [];

        private static readonly Validate<bool?> LitRule = value => value is null ? [Unfilled] : [Ticked];

        public string Street { get; set; } = "";

        [System.ComponentModel.DataAnnotations.Required] public string City { get; set; } = "";

        public bool? Lit { get; set; }

        public Door Front { get; set; } = new();

        public Door? Back { get; set; } = new();

        internal static bool IsRequired(string property) => property == nameof(Street);

        internal static bool IsOptional(string property) => property == nameof(Back);

        internal static Delegate? RuleOf(string property) => property switch
        {
            nameof(Street) => StreetRule,
            nameof(Lit) => LitRule,
            _ => null,
        };
    }

    private sealed class Door
    {
        public int Number { get; set; }

        internal static bool IsRequired(string property) => property == nameof(Number);
    }
}
