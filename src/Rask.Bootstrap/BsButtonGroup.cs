namespace Rask.Bootstrap;

// A Bootstrap button group: <div class="btn-group" role="group">. Holds BsButton children. Set
// Vertical for a stacked group, and Size for btn-group-sm / btn-group-lg.
public sealed class BsButtonGroup : BsBlock
{
    public bool? Vertical { get; set; }
    public BsSize? Size { get; set; }

    protected override RenderResult Render() => Div(
        Id: Id,
        Class: BsClass.Join(
            Vertical is true ? "btn-group-vertical" : "btn-group",
            Size is { } s && s.Suffix() is { } suffix ? $"btn-group-{suffix}" : null,
            Class),
        Role: "group")[Items];
}
