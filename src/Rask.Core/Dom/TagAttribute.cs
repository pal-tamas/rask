namespace Rask.Core;

/// <summary>
///     The tag an element type renders, and the chain entry that builds it. Repeat it for a type several tags
///     share, the way the DOM shares an interface: <c>h1</c>–<c>h6</c> are all one heading element. The entry
///     is the tag in PascalCase (<c>td</c> → <c>Td</c>) unless <see cref="Entry" /> names it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class TagAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Entry { get; set; }
}
