using FluentValidation;
using FluentValidation.Results;
using Rask.Core.Forms;


namespace Rask.Validation.FluentValidation.Tests;

// FluentValidation walks nested rules itself via .SetValidator(...) and RuleForEach(...). These
// tests prove our routing layer translates the dotted PropertyName paths back to the right
// (subInstance, terminal) FieldIdentifier so validation messages land at the bound field.
public partial class NestedValidationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task ValidateAsync_SubObjectRule_RoutesToSubInstance()
    {
        var p = new Person { Address = new Address { Street = "" } };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateAsync();

        Assert.Contains("Street required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Street")));
    }

    [Fact]
    public async Task ValidateAsync_DeepChain_RoutesToTerminalOwner()
    {
        var p = new Person
        {
            Address = new Address { Postal = new PostalInfo { Country = new Country { Code = "" } } }
        };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateAsync();

        Assert.Contains("Code required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!.Postal!.Country!, "Code")));
    }

    [Fact]
    public async Task ValidateAsync_RuleForEach_RoutesPerItem()
    {
        var alpha = new LineItem { Name = "" };
        var beta = new LineItem { Name = "ok" };
        var gamma = new LineItem { Name = "" };

        var p = new Person { Items = new List<LineItem> { alpha, beta, gamma } };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateAsync();

        Assert.Contains("Item name required",
            ctx.GetValidationMessages(new FieldIdentifier(alpha, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(beta, "Name")));
        Assert.Contains("Item name required",
            ctx.GetValidationMessages(new FieldIdentifier(gamma, "Name")));
        // Root model has no field-level errors keyed by these names.
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task ValidateFieldAsync_NestedField_OnlyTouchesThatField()
    {
        var alpha = new LineItem { Name = "" };
        var beta = new LineItem { Name = "" };
        var p = new Person { Address = new Address { Street = "" }, Items = new List<LineItem> { alpha, beta } };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(beta, "Name"));

        // Only beta's field gets the message — sibling list item alpha and the sub-object
        // Address.Street stay untouched even though they're also invalid.
        Assert.Contains("Item name required",
            ctx.GetValidationMessages(new FieldIdentifier(beta, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(alpha, "Name")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
    }

    [Fact]
    public async Task ValidateFieldAsync_SubObjectField_OnlyTouchesThatField()
    {
        var p = new Person { Name = "", Address = new Address { Street = "" } };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p.Address!, "Street"));

        Assert.Contains("Street required",
            ctx.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task ValidateFieldAsync_RootField_StillFastPath()
    {
        // Root-model fields use the MemberNameValidatorSelector fast path; this test pins
        // that the existing behavior is unchanged for the non-nested case.
        var p = new Person { Name = "" };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Name"));

        Assert.Contains("Name required",
            ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task ValidateAsync_FormLevelError_AttachesToRootEmptyField()
    {
        // RuleFor on the model itself (no property selector) — FV emits with PropertyName="".
        // We route empty paths to (root, "") so ValidationSummary picks them up.
        var p = new Person { Name = "BLOCKED" };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateAsync();

        Assert.Contains("Person is blocked",
            ctx.GetValidationMessages(new FieldIdentifier(p, string.Empty)));
    }

    [Fact]
    public async Task ValidateAsync_StaleIndexInError_FallsBackToFormLevel()
    {
        // Hand-craft an error whose property path can't resolve (no Items[7]). The router
        // must NOT crash and must NOT silently drop the message — it lands on the root's
        // form-level slot so it surfaces in ValidationSummary.
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" } } };
        var ctx = RegisterValidator(p, new StaleIndexValidator());

        await ctx.ValidateAsync();

        Assert.Contains(
            "stale-index error",
            ctx.GetValidationMessages(new FieldIdentifier(p, string.Empty)));
    }

    [Fact]
    public async Task FormPipeline_NestedField_FiresOnChange()
    {
        // End-to-end through the Form factory + Input handler dispatch path. Without the
        // model-graph pre-walk in Form.Model's setter, the Input bound to p.Address.Street
        // would resolve to a separate EditContext keyed by p.Address — different from the
        // form's EditContext where FluentValidationValidator self-registers — and the per-
        // keystroke ValidateFieldAsync call would land in an empty context, producing no
        // message.
        var p = new Person { Address = new Address { Street = "" } };
        EditContext? captured = null;

        var page = RaskTest.Render(() => Form(p)[
            FluentValidationValidator(new PersonValidator()),
            Input.Bind(() => p.Address!.Street),
            RaskTest.EditContextProbe(ctx => captured = ctx)
        ]);

        var changeId = page.HandlerId("change");
        Assert.NotNull(changeId);
        await page.InvokeAsync(changeId!, "{\"value\":\"\"}");

        Assert.NotNull(captured);
        Assert.Same(p, captured!.Model);
        Assert.Contains("Street required",
            captured.GetValidationMessages(new FieldIdentifier(p.Address!, "Street")));
    }

    private sealed class Person
    {
        public string Name { get; set; } = "Ada";
        public Address? Address { get; set; }
        public List<LineItem> Items { get; set; } = new();
    }

    private new sealed class Address
    {
        public string Street { get; set; } = "";
        public PostalInfo? Postal { get; set; }
    }

    private sealed class PostalInfo
    {
        public Country? Country { get; set; }
    }

    private sealed class Country
    {
        public string Code { get; set; } = "";
    }

    private sealed class LineItem
    {
        public string Name { get; set; } = "";
    }

    private sealed class PersonValidator : AbstractValidator<Person>
    {
        public PersonValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name required");
            RuleFor(x => x).Must(p => p.Name != "BLOCKED").WithMessage("Person is blocked");

            RuleFor(x => x.Address!).SetValidator(new AddressValidator()).When(x => x.Address is not null);
            RuleForEach(x => x.Items).SetValidator(new LineItemValidator());
        }
    }

    private sealed class AddressValidator : AbstractValidator<Address>
    {
        public AddressValidator()
        {
            RuleFor(x => x.Street).NotEmpty().WithMessage("Street required");
            RuleFor(x => x.Postal!).SetValidator(new PostalValidator()).When(x => x.Postal is not null);
        }
    }

    private sealed class PostalValidator : AbstractValidator<PostalInfo>
    {
        public PostalValidator() => RuleFor(x => x.Country!).SetValidator(new CountryValidator())
            .When(x => x.Country is not null);
    }

    private sealed class CountryValidator : AbstractValidator<Country>
    {
        public CountryValidator() => RuleFor(x => x.Code).NotEmpty().WithMessage("Code required");
    }

    private sealed class LineItemValidator : AbstractValidator<LineItem>
    {
        public LineItemValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Item name required");
    }

    // A validator that produces an error whose PropertyName can't be resolved (stale index).
    private sealed class StaleIndexValidator : AbstractValidator<Person>
    {
        public StaleIndexValidator()
        {
            RuleFor(x => x).Custom((model, ctx) =>
            {
                ctx.AddFailure(new ValidationFailure(
                    "Items[7].Name", "stale-index error"));
            });
        }
    }
}
