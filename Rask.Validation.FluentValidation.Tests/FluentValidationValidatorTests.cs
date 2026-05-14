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

    private static EditContext RegisterValidator(Person p, IValidator validator)
    {
        var ctx = new EditContext(p);
        using (EditContextScope.Push(ctx))
        {
            FluentValidationValidator(validator).ToHtml();
        }
        return ctx;
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
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
        public ThrowingValidator()
        {
            RuleFor(x => x.Name).Must(_ => throw new InvalidOperationException("boom"));
        }
    }
}
