using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation.Tests;

// Discovery, end to end. Nothing below calls RaskValidators.Register: the validators are declared as
// ordinary AbstractValidator<T> classes, the generator finds them at compile time and emits the
// registration, and the form asks for the validator of its model type and gets one.
//
// Each model here is used by this file ALONE. Sharing one with the manual-registration suite would let
// that suite's Register call decide what these tests see, and they would then pass without the
// generator having done anything at all.
public partial class GeneratedRegistrationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Declaring_a_validator_is_the_whole_registration()
    {
        var m = new DiscoveredModel { Title = "" };
        var ctx = Render(m);

        await ctx.ValidateAsync();

        Assert.Contains("Title is required.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(DiscoveredModel.Title))));
    }

    [Fact]
    public async Task Attributes_run_alongside_the_discovered_validator_and_come_first()
    {
        // Both passes apply to this model. DataAnnotations is the sync stage and the discovered
        // validator is the async one, so EditContext's existing per-field first-error-wins gating means
        // the attribute message is the one that shows on a field both of them fail.
        var m = new BothModel { Code = "" };
        var ctx = Render(m);

        await ctx.ValidateAsync();

        var messages = ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BothModel.Code)));
        Assert.Equal(["Code is required."], messages);
    }

    [Fact]
    public async Task Attributes_and_the_validator_both_surface_on_different_fields()
    {
        var m = new BothModel { Code = "abc", Quantity = 0 };
        var ctx = Render(m);

        await ctx.ValidateAsync();

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BothModel.Code))));
        Assert.Contains("Quantity must be at least 1.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(BothModel.Quantity))));
    }

    [Fact]
    public async Task A_MustAsync_rule_rides_the_same_discovery()
    {
        var m = new AsyncModel { Name = "taken" };
        var ctx = Render(m);

        await ctx.ValidateAsync();

        Assert.Contains("Name is already taken.",
            ctx.GetValidationMessages(new FieldIdentifier(m, nameof(AsyncModel.Name))));
    }

    [Fact]
    public void A_validator_needing_services_does_not_break_a_render_with_no_scope()
    {
        // The validator is only BUILT when validation runs, not when the form registers it. Building at
        // registration meant this render threw InvalidOperationException out of Render() — from generated
        // code the author never wrote — purely because a DI-constructed validator for this model exists
        // somewhere in the assembly. Test.Render with no provider is a supported shape.
        var m = new NeedsServiceModel { Name = "" };

        var ctx = Render(m);

        Assert.NotNull(ctx);
        Assert.Same(m, ctx.Model);
    }

    [Fact]
    public async Task A_model_with_no_validator_leaves_Validate_synchronous()
    {
        // A DiscoveredFieldValidator is an IAsyncFieldValidator, and one of those on the context makes
        // the synchronous EditContext.Validate() throw. Registering it unconditionally would have made
        // every form in every app pay that for a validator it does not have.
        var ctx = Render(new UnvalidatedModel { Anything = "" });

        Assert.False(ctx.HasAsyncValidators);
        Assert.True(ctx.Validate());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_model_with_no_validator_is_left_alone()
    {
        var m = new UnvalidatedModel { Anything = "" };
        var ctx = Render(m);

        await ctx.ValidateAsync();

        Assert.False(ctx.HasValidationMessages());
    }

    private EditContext Render<T>(T model) where T : class
    {
        EditContext? ctx = null;
        Test.Render(() => Form.Model(model)[
            Test.EditContextProbe(c => ctx = c)
        ]);

        return ctx!;
    }

    internal sealed class DiscoveredModel
    {
        public string Title { get; set; } = "";
    }

    internal sealed class DiscoveredModelValidator : AbstractValidator<DiscoveredModel>
    {
        public DiscoveredModelValidator() =>
            RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required.");
    }

    internal sealed class BothModel
    {
        [Required(ErrorMessage = "Code is required.")]
        public string Code { get; set; } = "";

        public int Quantity { get; set; }
    }

    internal sealed class BothModelValidator : AbstractValidator<BothModel>
    {
        public BothModelValidator()
        {
            RuleFor(x => x.Code).NotEmpty().WithMessage("Code came from FluentValidation.");
            RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
        }
    }

    internal sealed class AsyncModel
    {
        public string Name { get; set; } = "";
    }

    internal sealed class AsyncModelValidator : AbstractValidator<AsyncModel>
    {
        public AsyncModelValidator() =>
            RuleFor(x => x.Name)
                .MustAsync(static async (name, ct) =>
                {
                    await Task.Yield();
                    return name != "taken";
                })
                .WithMessage("Name is already taken.");
    }

    internal sealed class UnvalidatedModel
    {
        public string Anything { get; set; } = "";
    }

    internal sealed class NeedsServiceModel
    {
        public string Name { get; set; } = "";
    }

    internal interface INameRule
    {
        bool Allows(string name);
    }

    internal sealed class NeedsServiceModelValidator : AbstractValidator<NeedsServiceModel>
    {
        public NeedsServiceModelValidator(INameRule rule) =>
            RuleFor(x => x.Name).Must(rule.Allows).WithMessage("Not allowed.");
    }
}
