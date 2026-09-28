using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class CustomAttributeModel
{
    [Required(ErrorMessage = "Username is required.")]
    [NotBanned(ErrorMessage = "\"{0}\" isn't available.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Password is required.")]
    [StrongPassword(ErrorMessage = "Password must be at least 8 characters and mix letters and digits.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password.")]
    [MatchesProperty(nameof(Password), ErrorMessage = "Passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";
}
