using FluentValidation;
using Rask.Core.Forms;

namespace Rask.Validation.FluentValidation.Tests;

public class FluentValidationValidatorTests
{
    [Fact]
    public async Task ValidateAsync_PopulatesMessages_PerOffendingProperty()
    {
        var p = new Person { Name = "", Age = 0 };
        var ctx = RegisterValidator(p, new PersonValidator());

        var ok = await ctx.ValidateAsync();

        Assert.False(ok);
        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        Assert.Contains("Age must be positive", ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public async Task ValidateFieldAsync_ScopedToSingleProperty()
    {
        var p = new Person { Name = "", Age = 999 };
        var ctx = RegisterValidator(p, new PersonValidator());

        // Age has rules but Name's "" should not bleed in.
        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Age"));

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
        // Age=999 is fine for our rule (just >0), so no messages.
        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Age")));
    }

    [Fact]
    public async Task ValidateFieldAsync_AddsMessageForFailingProperty()
    {
        var p = new Person { Name = "", Age = 5 };
        var ctx = RegisterValidator(p, new PersonValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Name"));

        Assert.Contains("Name required", ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task ValidateFieldAsync_ExceptionInValidator_AddsGenericMessage()
    {
        var p = new Person { Name = "throw", Age = 1 };
        var ctx = RegisterValidator(p, new ThrowingValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Name"));

        var msgs = ctx.GetValidationMessages(new FieldIdentifier(p, "Name"));
        Assert.Contains(msgs, m => m.Contains("could not be completed"));
    }

    [Fact]
    public async Task MustAsync_FailingRule_SurfacesMessageForField()
    {
        // The async rule path (MustAsync) must be awaited and its failure surfaced per field, exactly like a
        // sync rule — this is the headline reason FluentValidation registers an IAsyncFieldValidator.
        var p = new Person { Name = "taken", Age = 1 };
        var ctx = RegisterValidator(p, new AsyncNameValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Name"));

        Assert.Contains("Name taken", ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task MustAsync_PassingRule_LeavesFieldClean()
    {
        var p = new Person { Name = "free", Age = 1 };
        var ctx = RegisterValidator(p, new AsyncNameValidator());

        await ctx.ValidateFieldAsync(new FieldIdentifier(p, "Name"));

        Assert.Empty(ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    [Fact]
    public async Task ValidateAsync_RunsAsyncRuleAcrossTheWholeForm()
    {
        var p = new Person { Name = "taken", Age = 1 };
        var ctx = RegisterValidator(p, new AsyncNameValidator());

        var ok = await ctx.ValidateAsync();

        Assert.False(ok);
        Assert.Contains("Name taken", ctx.GetValidationMessages(new FieldIdentifier(p, "Name")));
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private sealed class AsyncNameValidator : AbstractValidator<Person>
    {
        public AsyncNameValidator() =>
            RuleFor(x => x.Name).MustAsync(async (name, _) =>
            {
                await Task.Yield();
                return name != "taken";
            }).WithMessage("Name taken");
    }

    private sealed class PersonValidator : AbstractValidator<Person>
    {
        public PersonValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name required");
            RuleFor(x => x.Age).GreaterThan(0).WithMessage("Age must be positive");
        }
    }

    private sealed class ThrowingValidator : AbstractValidator<Person>
    {
        public ThrowingValidator() => RuleFor(x => x.Name).Must(_ => throw new InvalidOperationException("boom"));
    }
}
