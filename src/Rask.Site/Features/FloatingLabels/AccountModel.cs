using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class AccountModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Full name must be 2–60 characters.")]
    public string? FullName { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? Email { get; set; }

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120.")]
    public int? Age { get; set; }

    [Required(ErrorMessage = "Pick a plan.")]
    public string? Plan { get; set; }

    [StringLength(200, ErrorMessage = "Bio must be 200 characters or fewer.")]
    public string? Bio { get; set; }
}
