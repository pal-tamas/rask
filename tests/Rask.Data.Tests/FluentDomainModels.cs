using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using FluentValidation;

namespace Rask.Data.Tests;

// An aggregate written the FluentValidation way: its rules in its own static Rules, beside a row whose rules are its
// own, and a value object that states its rule the same way (FluentDomainValidationTests).
public sealed class Harbour : Aggregate<Guid, Versioned, Model, Deletable>
{
    private readonly List<Quay> _quays = [];

    private Harbour() { } // EF materialization

    public string Name { get; private set; } = "";

    public string? Email { get; private set; }

    public string? Code { get; private set; }

    [MaxLength(50)]
    public string Region { get; private set; } = "";

    public decimal Depth { get; private set; }

    public HarbourCode? Sign { get; private set; }

    public IReadOnlyCollection<Quay> Quays => _quays;

    public static Harbour Open(string name, string? email = null) =>
        new() { Id = Guid.CreateVersion7(), Name = name, Email = email, Code = "H", Region = "North" };

    public void Rename(string name) => Name = name;

    public void Write(string? email) => Email = email;

    public void Mark(HarbourCode sign) => Sign = sign;

    public void Recode(string code) => Code = code;

    public Quay Add(decimal length)
    {
        var quay = Quay.Of(length);
        _quays.Add(quay);
        return quay;
    }

    internal static void Rules(InlineValidator<Harbour> rules)
    {
        rules.RuleFor(h => h.Name).NotEmpty().WithMessage("A harbour has a name.").MaximumLength(20);
        rules.RuleFor(h => h.Email).EmailAddress().WithMessage("Not an address.").MaximumLength(254);
        rules.RuleFor(h => h.Code).NotEmpty().MaximumLength(5)
            .MustAsync(static async (code, ct) =>
            {
                await Task.Yield();
                return !string.Equals(code, "TAKEN", StringComparison.Ordinal);
            }).WithMessage("That code is taken.");
#pragma warning disable RASK182 // on purpose: the attribute wins for the column (An_attribute_on_the_property_wins_over_the_rule_for_its_column)
        rules.RuleFor(h => h.Region).MaximumLength(80);
#pragma warning restore RASK182
        rules.RuleFor(h => h.Depth).PrecisionScale(8, 2, false);
    }
}

public sealed class Quay : Entity<Guid>
{
    private Quay() { } // EF materialization

    public decimal Length { get; private set; }

    public void Resize(decimal length) => Length = length;

    internal static Quay Of(decimal length) => new() { Id = Guid.CreateVersion7(), Length = length };

    internal static void Rules(InlineValidator<Quay> rules) =>
        rules.RuleFor(b => b.Length).GreaterThan(0).WithMessage("A quay has a length.");
}

// What the generator writes for a value object with a static Rules (this suite runs the batteries' generators only).
public sealed partial record HarbourCode(string Value) : Text(Value, MaxLength: 10)
{
    protected override IEnumerable<Problem> Rules() => RaskValidators.Problems(this);

    internal static void Stated(InlineValidator<HarbourCode> rules) =>
        rules.RuleFor(c => c.Value).Must(static v => v.ToUpperInvariant() == v).WithMessage("A sign is in capitals.");
}

// The value object's Stated rules, registered as the value-object generator's output would be (HarbourCode above is a
// hand-written stand-in for it). Harbour's and Quay's own Rules are registered by the validator registry's generator,
// which this suite runs since the value-object generator moved into Rask.Generators — listing them here too asked
// every rule twice.
internal static class FluentDomainRegistration
{
#pragma warning disable CA2255 // the registration a generated module initializer makes in an app
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() =>
        RaskValidators.Replace(
            typeof(FluentDomainRegistration),
            [
                RaskValidators.Inline<HarbourCode>(HarbourCode.Stated),
            ]);
}
