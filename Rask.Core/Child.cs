namespace Rask.Core;

public readonly struct Child
{
    public Child(Component component) => Component = component;
    public Child(string text) => Component = new Text(text);

    public Component Component { get; }

    public static implicit operator Child(Component component) => new(component);
    public static implicit operator Child(string text) => new(text);
}
