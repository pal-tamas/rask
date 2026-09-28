using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class SignupModel
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Username must be 3–20 characters.")]
    public string Username { get; set; } = "";
}
