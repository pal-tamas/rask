using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class TodoForm
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Title must be 1–120 characters.")]
    public string Title { get; set; } = "";
}
