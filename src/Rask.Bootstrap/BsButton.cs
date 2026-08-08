namespace Rask.Bootstrap;

// A Bootstrap button. Wraps the core Button() component, composing the .btn classes from the typed
// Color/Outline/Size/Active props and forwarding the common button attributes/handlers — it does not
// re-implement the <button> element. Type defaults to "button" so a BsButton never implicitly submits
// an enclosing form; pass Type:"submit" for a submit button. For a link styled as a button, use
// A(Class:"btn btn-primary").
public sealed partial class BsButton : BsBlock
{
    public BsColor? Color { get; set; }

    // Outline variant (btn-outline-{color}); ignored when Color is null.
    public bool? Outline { get; set; }

    public BsSize? Size { get; set; }

    // Toggle/pressed state: adds .active and aria-pressed="true".
    public bool? Active { get; set; }

    public string? Type { get; set; }
    public bool? Disabled { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public new string? Style { get; set; }
    public IReadOnlyDictionary<string, string?>? Aria { get; set; }

    public Handler? OnClick { get; set; }
    public HandlerAsync? OnClickAsync { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "btn",
            Color is { } c ? c.Button(Outline is true) : null,
            Size is { } s ? s.ButtonSize() : null,
            Active is true ? "active" : null,
            Class);

        var aria = Active is true ? BsClass.WithAria(Aria, "pressed", "true") : Aria;

        // Both handlers forward straight through to the native Button (the consumer sets at most one;
        // RASK027 enforces that at their call site). The set delegate passes raw to the DOM element,
        // whose handler-owner resolution re-renders the parent.
        return Button(Id: Id, Class: cls, Style: Style, Type: Type ?? "button",
            Disabled: Disabled, Name: Name, Value: Value, Aria: aria,
            OnClick: OnClick?.Fn, OnClickAsync: OnClickAsync?.Fn)[Items];
    }
}
