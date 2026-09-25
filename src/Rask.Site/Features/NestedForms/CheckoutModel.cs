using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class CheckoutModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(60)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Looks like an invalid email.")]
    public string Email { get; set; } = "";

    public AddressModel Address { get; set; } = new();
}
