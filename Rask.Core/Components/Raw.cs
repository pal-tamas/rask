namespace Rask.Core.Components;

public sealed class Raw : Component
{
    public Raw() { }
    public Raw(string html) => Value = html;

    public string? Value { get; set; }
}
