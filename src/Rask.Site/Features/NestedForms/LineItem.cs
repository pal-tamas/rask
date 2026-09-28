using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class LineItem
{
    // Stable per-instance key for keyed row diffing (not bound to any input, no validation attrs).
    public Guid Id { get; } = Guid.NewGuid();

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(80)]
    public string Description { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;
}
