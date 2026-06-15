using System.ComponentModel.DataAnnotations;

namespace Rask.Example.Shared.Features;

public sealed class RegistrationModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "Name must be 2–40 characters.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string Email { get; set; } = "";

    [Range(13, 120, ErrorMessage = "Age must be between 13 and 120.")]
    public int Age { get; set; }

    [Required(ErrorMessage = "Pick a plan.")]
    public string Plan { get; set; } = "";
}

// Resolved from the form's render-scoped IServiceProvider by [NotBanned]'s GetValidationResult.
public interface IBannedWordService
{
    IReadOnlyCollection<string> Words { get; }
}

public sealed class BannedWordService : IBannedWordService
{
    public IReadOnlyCollection<string> Words { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "root", "test" };
}
