using System.ComponentModel.DataAnnotations;

namespace Rask.Site.Features;

public sealed class TaskModel
{
    [Required(ErrorMessage = "Title is required.")]
    public string Title { get; set; } = "";
}
