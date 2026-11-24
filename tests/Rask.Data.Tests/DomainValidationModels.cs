using System.ComponentModel.DataAnnotations;

namespace Rask.Data.Tests;

// An aggregate written the DataAnnotations way, with a rule of its own over its rows and one about its delete: what
// every save of it is asked (DomainValidationTests).
public sealed class Voyage : Aggregate<Guid, Versioned, Model, Deletable>
{
    private readonly List<VoyageLeg> _legs = [];

    private Voyage() { } // EF materialization

    [Required, StringLength(10)]
    public string Title { get; private set; } = "";

    [Range(1, 100)]
    public int Seats { get; private set; }

    public bool Sealed { get; private set; }

    public IReadOnlyCollection<VoyageLeg> Legs => _legs;

    public static Voyage Plan(string title, int seats) => new() { Id = Guid.CreateVersion7(), Title = title, Seats = seats };

    public void Rename(string title) => Title = title;

    public void Book(int seats) => Seats = seats;

    public void Seal() => Sealed = true;

    public VoyageLeg Add(string port, int days)
    {
        var leg = VoyageLeg.To(port, days);
        _legs.Add(leg);
        return leg;
    }

    public void Remove(VoyageLeg leg) => _legs.Remove(leg);

    private IEnumerable<Problem> Validate()
    {
        if (Sealed && _legs.Count == 0)
        {
            yield return Problem.At(Legs, "A sealed voyage needs a leg.");
        }
    }

    private async IAsyncEnumerable<Problem> ValidateDelete()
    {
        await Task.Yield();
        if (Sealed)
        {
            yield return Problem.Whole("A sealed voyage is kept.");
        }
    }
}

public sealed class VoyageLeg : Entity<Guid>
{
    private VoyageLeg() { } // EF materialization

    [StringLength(5)]
    public string Port { get; private set; } = "";

    public int Days { get; private set; }

    public void Stay(int days) => Days = days;

    internal static VoyageLeg To(string port, int days) => new() { Id = Guid.CreateVersion7(), Port = port, Days = days };

    private IEnumerable<Problem> Validate()
    {
        if (Days < 0)
        {
            yield return Problem.At(Days, "A leg cannot take negative days.");
        }
    }
}

// An aggregate that checks itself as .NET's IValidatableObject does, and whose attribute asks the work's services.
public sealed class Cabin : Aggregate<Guid>, IValidatableObject
{
    private Cabin() { } // EF materialization

    [NotForbidden]
    public string Name { get; private set; } = "";

    public int Berths { get; private set; }

    public static Cabin Fit(string name, int berths) => new() { Id = Guid.CreateVersion7(), Name = name, Berths = berths };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Berths > 4)
        {
            yield return new ValidationResult("At most four berths.", [nameof(Berths)]);
        }
    }
}

/// <summary>The words the work in progress forbids, as a service a validation attribute resolves.</summary>
public sealed class ForbiddenWords(params string[] words)
{
    public bool Forbids(string? text) => text is not null && words.Contains(text, StringComparer.OrdinalIgnoreCase);
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotForbiddenAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        validationContext.GetService(typeof(ForbiddenWords)) is ForbiddenWords forbidden && forbidden.Forbids(value as string)
            ? new ValidationResult("That word is forbidden.", [validationContext.MemberName!])
            : ValidationResult.Success;
}

// An aggregate whose form model the app completes: an awaited attribute on a property, and the model's own rule.
public sealed class Charter : Aggregate<Guid, Model>
{
    private Charter() { } // EF materialization

    [Required, NameNotTaken]
    public string Name { get; private set; } = "";

    public int Guests { get; private set; }

    public void Rename(string name) => Name = name;
}

public sealed partial class CharterModel : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Guests > 12)
        {
            yield return new ValidationResult("At most twelve guests.", [nameof(Guests)]);
        }
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NameNotTakenAttribute() : AsyncValidationAttribute("{0} is taken.")
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        throw new NotSupportedException("Asked only asynchronously.");

    protected override async Task<ValidationResult?> IsValidAsync(
        object? value, ValidationContext validationContext, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return value is "Endeavour"
            ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName), [validationContext.MemberName!])
            : ValidationResult.Success;
    }
}

// A form-written aggregate whose attribute asks the work's services.
public sealed class Mooring : Aggregate<Guid, Model>
{
    private Mooring() { } // EF materialization

    [NotForbidden]
    public string Name { get; private set; } = "";
}
