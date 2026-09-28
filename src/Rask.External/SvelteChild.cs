using Rask.Core;

namespace Rask.External;

/// <summary>A child a Svelte island can render: a Svelte island, text, a number or a date.</summary>
public readonly struct SvelteChild
{
    private SvelteChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator SvelteChild(SvelteComponent? island) => new(island);
    public static implicit operator SvelteChild(string? text) => new(text);
    public static implicit operator SvelteChild(int value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(long value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(double value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(float value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(bool value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(char value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(TimeSpan value) => new(ChildText.Of(value));
}
