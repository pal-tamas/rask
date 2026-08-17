namespace Rask.Bootstrap;

// A Bootstrap button. Wraps the core Button() component, composing the .btn classes from the typed
// Color/Outline/Size/Active props and forwarding the common button attributes/handlers — it does not
// re-implement the <button> element. Type defaults to "button" so a BsButton never implicitly submits
// an enclosing form; pass Type:"submit" for a submit button. For a link styled as a button, use
// A(Class:"btn btn-primary").

/// <summary>
///     A Bootstrap button. Prefer it over a raw <c>Button</c> with hand-written classes: the colour, size
///     and outline variants are typed, so a misspelled class cannot slip through. For something that
///     navigates rather than acts, use <c>BsLink</c> — a link styled as a button is still a link.
/// </summary>
public sealed partial class BsButton : BsBlock
{
    /// <summary>
    ///     The semantic colour — <c>Primary</c> for the one action you want taken, <c>Secondary</c> for the
    ///     rest, <c>Danger</c> for anything destructive.
    /// </summary>
    public BsColor? Color { get; set; }

    // Outline variant (btn-outline-{color}); ignored when Color is null.

    /// <summary>
    ///     Draws the button outlined rather than filled, for a lower-emphasis action beside a solid one.
    /// </summary>
    public bool? Outline { get; set; }

    /// <summary>Makes the button smaller or larger than the default.</summary>
    public BsSize? Size { get; set; }

    // Toggle/pressed state: adds .active and aria-pressed="true".

    /// <summary>Renders the pressed state.</summary>
    public bool? Active { get; set; }

    /// <summary>
    ///     <c>submit</c> (the default inside a form), <c>reset</c>, or <c>button</c>. Set it explicitly —
    ///     an unset type inside a form submits it.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     Makes the button unclickable. A disabled control cannot be focused, so it cannot explain why it
    ///     is disabled; consider leaving it enabled and reporting the reason instead.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>The name submitted with the form when this button submits it.</summary>
    public string? Name { get; set; }

    /// <summary>The value submitted alongside <c>Name</c>.</summary>
    public string? Value { get; set; }
    /// <summary>
    ///     Inline CSS on the rendered element. Reach for a Bootstrap utility class through <c>Class</c>
    ///     first — inline style beats every stylesheet rule and cannot be overridden by a theme, so keep it
    ///     for values only known at runtime.
    /// </summary>
    public new string? Style { get; set; }
    /// <summary>
    ///     ARIA states and properties on the rendered element. Each entry emits <c>aria-{key}="{value}"</c>,
    ///     so <c>.Aria("label", "Close")</c> renders <c>aria-label="Close"</c> — give the key without the
    ///     prefix.
    ///     <para>
    ///         State belongs here as much as labels: <c>aria-expanded</c> and <c>aria-current</c> have to
    ///         change as the component does, or assistive technology is told the opposite of what is shown.
    ///     </para>
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Aria { get; set; }

    /// <summary>Runs when the button is clicked, then re-renders the owning component.</summary>
    public Action? OnClick { get; set; }

    /// <summary>
    ///     Runs when the button is clicked, asynchronously, then re-renders the owning component.
    /// </summary>
    public Func<Task>? OnClickAsync { get; set; }

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
        return Button
            .Id(Id)
            .Class(cls)
            .Style(Style)
            .Type(Type ?? "button")
            .Disabled(Disabled)
            .Name(Name)
            .Value(Value)
            .Aria(aria)
            .OnClick(OnClick)
            .OnClickAsync(OnClickAsync)[Items];
    }
}
