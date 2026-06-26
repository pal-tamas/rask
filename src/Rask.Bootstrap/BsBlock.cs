namespace Rask.Bootstrap;

// Base for composite Bootstrap components — those that render a wrapper element around their
// children (Card, Alert, ListGroup, …) rather than being a single styled element. It exposes the
// Id/Class pass-through that the generated factory surfaces as optional parameters, without pulling
// in the full HTML-element attribute/event surface that Element carries. Abstract, so the factory
// generator skips it; subclasses inherit Id/Class as leading optional factory params.
public abstract class BsBlock : Component
{
    public string? Id { get; set; }
    public string? Class { get; set; }

    // The children passed via the indexer, or an empty sequence.
    private protected IEnumerable<Child> Items => Children ?? [];

    // Renders a <div class="{baseClass} {Class}" id="{Id}"> wrapper around the children — the shape
    // most container parts share (card sections, etc.).
    private protected RenderResult Wrap(string baseClass) =>
        Div(Id: Id, Class: BsClass.Join(baseClass, Class))[Items];

    // The children followed by extra trailing children (e.g. an alert's close button), as one
    // sequence for the children indexer (the `..` spread is unsupported — pass an enumerable).
    private protected IEnumerable<Child> ItemsWith(params Child[] trailing)
    {
        foreach (var item in Items)
        {
            yield return item;
        }

        foreach (var item in trailing)
        {
            yield return item;
        }
    }
}
