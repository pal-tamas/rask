namespace Rask.Core;

/// <summary>
///     A ref to an element of MDN interface <typeparamref name="T" />. Covariant, so a ref to a dialog is a ref to an
///     <c>HTMLElement</c> too, and carries its members (<c>Focus()</c>).
/// </summary>
/// <typeparam name="T">The element's MDN interface.</typeparam>
public interface IElementRef<out T>
    where T : Element
{
    // Only Rask's refs implement it: the generated members reach the element through this.
    internal ElementRef Target { get; }

    // The element the ref was last put on, as its type.
    internal T? Attached { get; }
}
