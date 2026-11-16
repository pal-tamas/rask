using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class NewsletterModel
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [UniqueEmail]
    public string Email { get; set; } = "";
}
