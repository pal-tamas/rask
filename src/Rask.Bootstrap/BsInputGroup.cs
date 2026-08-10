namespace Rask.Bootstrap;

// A Bootstrap input group: <div class="input-group">. Wrap a control plus add-ons / buttons; Size maps
// to input-group-sm / input-group-lg.
public sealed partial class BsInputGroup : BsBlock
{
    public BsSize? Size { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(
            "input-group",
            Size is { } s && s.Suffix() is { } suffix ? $"input-group-{suffix}" : null,
            Class))[Items];
}

// A text add-on inside an input group: <span class="input-group-text"> (e.g. "@", "$", a unit).
public sealed partial class BsInputGroupText : BsBlock
{
    protected override Component? Render() =>
        Span.Id(Id).Class(BsClass.Join("input-group-text", Class))[Items];
}
