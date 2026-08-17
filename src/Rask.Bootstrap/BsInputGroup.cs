namespace Rask.Bootstrap;

// A Bootstrap input group: <div class="input-group">. Wrap a control plus add-ons / buttons; Size maps
// to input-group-sm / input-group-lg.

/// <summary>
///     Joins a control to an adjacent add-on — a currency symbol, a unit, a button — as one visual field.
/// </summary>
public sealed partial class BsInputGroup : BsBlock
{
    /// <summary>Makes the whole group smaller or larger than the default.</summary>
    public BsSize? Size { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(
            "input-group",
            Size is { } s && s.Suffix() is { } suffix ? $"input-group-{suffix}" : null,
            Class))[Items];
}

// A text add-on inside an input group: <span class="input-group-text"> (e.g. "@", "$", a unit).

/// <summary>
///     A text add-on inside a <c>BsInputGroup</c>.
/// </summary>
public sealed partial class BsInputGroupText : BsBlock
{
    protected override Component? Render() =>
        Span.Id(Id).Class(BsClass.Join("input-group-text", Class))[Items];
}
