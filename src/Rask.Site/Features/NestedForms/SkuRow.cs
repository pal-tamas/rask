using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class SkuRow
{
    // Stable per-instance key for keyed row diffing — survives the up/down reorder.
    public Guid Id { get; } = Guid.NewGuid();

    [Required(ErrorMessage = "SKU is required.")]
    [RegularExpression("^[A-Z0-9-]{3,12}$", ErrorMessage = "Use uppercase letters, digits, and dashes (3-12 chars).")]
    public string Code { get; set; } = "";

    [Range(0.01, 99999.99, ErrorMessage = "Price must be greater than 0.")]
    public decimal Price { get; set; } = 0.01m;
}
