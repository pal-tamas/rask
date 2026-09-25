using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class AddressModel
{
    [Required(ErrorMessage = "Street is required.")]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "City is required.")]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Country is required.")]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "Use the ISO 2-letter code.")]
    public string Country { get; set; } = "";
}
