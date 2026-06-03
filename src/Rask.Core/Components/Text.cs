namespace Rask.Core.Components;

public sealed class Text : Component
{
    public Text() { }
    public Text(string value) => Value = value;

    public string? Value { get; set; }
}
