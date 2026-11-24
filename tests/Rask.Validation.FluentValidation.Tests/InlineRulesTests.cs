using System.Text.RegularExpressions;
using FluentValidation;
using Rask.Core.Forms;
using Rask.Values;

namespace Rask.FluentValidationTests;

// A type's own `static void Rules(InlineValidator<T> rules)`, found by the generator like an AbstractValidator<T> class:
// the rules run on a form, run together with a class beside them, and state the hints their DataAnnotations twins do.
public partial class InlineRulesTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_private_static_Rules_method_is_the_whole_registration()
    {
        var m = new Supplier { Name = "" };
        var ctx = Render(m);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Contains("Name is required.", ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Supplier.Name))));
    }

    [Fact]
    public async Task Rules_and_a_validator_class_for_the_same_type_both_run()
    {
        var m = new Both { Code = "", Quantity = 0 };
        var ctx = Render(m);

        await ctx.Validate(TestContext.Current.CancellationToken);

        Assert.Contains("Code is required.", ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Both.Code))));
        Assert.Contains("Quantity must be positive.", ctx.GetValidationMessages(new FieldIdentifier(m, nameof(Both.Quantity))));
    }

    [Fact]
    public void A_rule_states_the_hints_its_attribute_twin_would()
    {
        var name = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Name));
        var email = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Email));
        var rating = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Rating));
        var price = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Price));

        var stock = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Stock));

        Assert.Equal(new StatedFacets(Required: true, MaxLength: 200, MinLength: 2), name);
        Assert.Equal(new StatedFacets(MaxLength: 254, Email: true), email);
        Assert.Equal(new StatedFacets(Min: 1, Max: 5), rating);
        Assert.Equal(new StatedFacets(Precision: 10, Scale: 2, Min: 0), price);
        Assert.Equal(new StatedFacets(Min: 1), stock);
    }

    [Fact]
    public void A_bound_input_is_told_its_rules_as_an_attribute_would_tell_it()
    {
        var m = new Supplier();

        var name = Input.Bind(() => m.Name).ToHtml();
        var email = Input.Bind(() => m.Email).ToHtml();

        Assert.Contains("maxlength=\"200\"", name, StringComparison.Ordinal);
        Assert.Contains("minlength=\"2\"", name, StringComparison.Ordinal);
        Assert.Contains("aria-required=\"true\"", name, StringComparison.Ordinal);
        Assert.Contains("type=\"email\"", email, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-required", email, StringComparison.Ordinal);
    }

    [Fact]
    public void A_matches_rule_states_its_pattern_unless_its_options_read_otherwise()
    {
        var plate = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Plate));
        var tag = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Tag));

        var m = new Supplier();
        var html = Input.Bind(() => m.Plate).ToHtml();

        Assert.Equal(new StatedFacets(Pattern: "[A-Z]{3}"), plate);
        Assert.Null(tag.Pattern);
        Assert.Contains("pattern=\"[A-Z]{3}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conditional_rule_states_no_hint()
    {
        var facets = StatedRules.FacetsOf(typeof(Supplier), nameof(Supplier.Note));

        var asked = facets.IsEmpty;

        Assert.True(asked);
    }

    [Fact]
    public void A_validator_class_states_its_hints_too()
    {
        var facets = StatedRules.FacetsOf(typeof(Both), nameof(Both.Code));

        var required = facets.Required;

        Assert.True(required);
    }

    [Fact]
    public async Task The_save_seam_asks_only_the_members_it_is_handed()
    {
        var m = new Supplier { Name = "", Email = "not an address" };

        var failures = await StatedRules.Check(m, ["Email"], null, TestContext.Current.CancellationToken);

        var failure = Assert.Single(failures);
        Assert.Equal(["Email"], failure.Fields);
    }

    [Fact]
    public void A_value_objects_rules_run_after_its_base_and_name_the_member()
    {
        var refused = Test.Refuses(() => _ = new Opening(null, -1m));

        var said = refused.Failures.SelectMany(static f => f.Fields).ToList();

        Assert.Contains(said, static f => f.EndsWith("Quantity", StringComparison.Ordinal));
        Assert.Contains(said, static f => f.EndsWith("Date", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_value_objects_base_rule_and_its_own_rule_are_both_reported()
    {
        var refused = Test.Refuses(() => _ = new Handle("this handle is far too long"));

        var messages = refused.Failures.Select(static f => f.Message).ToList();

        Assert.Equal(2, messages.Count);
        Assert.Contains("A handle has no spaces.", messages);
    }

    [Theory]
    [InlineData("", "The specified condition was not met.")]
    [InlineData("hu-HU", "A megadott feltétel nem teljesült.")]
    public void A_value_objects_own_value_is_not_named_in_a_rules_own_sentence(string culture, string said)
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            var refused = Test.Refuses(() => _ = new Slug("Upper"));

            var message = Assert.Single(refused.Failures).Message;

            Assert.Equal(said, message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void A_value_object_its_rules_accept_is_made()
    {
        var handle = new Handle("tamas");

        var kept = handle.Value;

        Assert.Equal("tamas", kept);
    }

    [Fact]
    public void A_value_objects_rules_state_its_field_shape()
    {
        var shape = StatedRules.Shape(typeof(Handle), "Value", new FieldShape(FieldKind.Text, MaxLength: 12));

        var lengths = (shape.MaxLength, shape.MinLength);

        Assert.Equal((12, 3), lengths);
    }

    private EditContext Render<T>(T model)
        where T : class
    {
        EditContext? ctx = null;
        Page.Render(() => Form.Model(model)[
            Test.EditContextProbe(c => ctx = c)
        ]);

        return ctx!;
    }

    internal sealed class Supplier
    {
        public string Name { get; set; } = "";

        public string? Email { get; set; }

        public int Rating { get; set; } = 3;

        public decimal Price { get; set; }

        public int Stock { get; set; } = 1;

        public string? Note { get; set; }

        public bool Special { get; set; }

        public string? Plate { get; set; }

        public string? Tag { get; set; }

        static void Rules(InlineValidator<Supplier> rules)
        {
            rules.RuleFor(s => s.Name).NotEmpty().WithMessage("Name is required.").Length(2, 200);
            rules.RuleFor(s => s.Email).EmailAddress().MaximumLength(254);
            rules.RuleFor(s => s.Rating).InclusiveBetween(1, 5);
            rules.RuleFor(s => s.Price).PrecisionScale(10, 2, false).GreaterThanOrEqualTo(0);
            rules.RuleFor(s => s.Stock).GreaterThan(0);
            rules.RuleFor(s => s.Note).NotEmpty().MaximumLength(10).When(s => s.Special);
            rules.RuleFor(s => s.Plate).Matches("^[A-Z]{3}$");
            rules.RuleFor(s => s.Tag).Matches("^[a-z]+$", RegexOptions.IgnoreCase);
        }
    }

    internal sealed class Both
    {
        public string Code { get; set; } = "";

        public int Quantity { get; set; }

        internal static void Rules(InlineValidator<Both> rules) =>
            rules.RuleFor(b => b.Code).NotEmpty().WithMessage("Code is required.");
    }

    internal sealed class BothValidator : AbstractValidator<Both>
    {
        public BothValidator() => RuleFor(b => b.Quantity).GreaterThan(0).WithMessage("Quantity must be positive.");
    }
}

public sealed partial record Opening(DateOnly? Date, decimal? Quantity) : Value
{
    static void Rules(InlineValidator<Opening> rules)
    {
        rules.RuleFor(o => o.Date).NotNull();
        rules.RuleFor(o => o.Quantity).GreaterThanOrEqualTo(0);
    }
}

public sealed partial record Handle(string Value) : Rask.Values.Text(Value, MaxLength: 12)
{
    static void Rules(InlineValidator<Handle> rules) =>
        rules.RuleFor(h => h.Value).MinimumLength(3).Must(static v => !v.Contains(' ', StringComparison.Ordinal)).WithMessage("A handle has no spaces.");
}

public sealed partial record Slug(string Value) : Rask.Values.Text(Value, MaxLength: 40)
{
    static void Rules(InlineValidator<Slug> rules) =>
        rules.RuleFor(s => s.Value).Must(static v => string.Equals(v, v.ToLowerInvariant(), StringComparison.Ordinal));
}
