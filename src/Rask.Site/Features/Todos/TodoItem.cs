namespace Rask.Site.Features;

public sealed class TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}
