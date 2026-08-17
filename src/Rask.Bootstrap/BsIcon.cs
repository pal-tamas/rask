namespace Rask.Bootstrap;

// A Bootstrap icon: <i class="bi bi-{name}">. Name is a typed enum of every Bootstrap Icons glyph, so
// icon names are discoverable and compile-checked (no string typos). Decorative by default
// (aria-hidden="true"); set AriaLabel to expose the icon to assistive tech as an image.

/// <summary>
///     A Bootstrap icon, named from a typed set so a misspelling is a compile error. Decorative by default
///     — give it an <c>AriaLabel</c> only when the icon is the sole carrier of meaning.
/// </summary>
public sealed partial class BsIcon : BsBlock
{
    /// <summary>Which icon to render, from the generated set.</summary>
    public BsIconName Name { get; set; }

    /// <summary>The icon's semantic colour.</summary>
    public BsColor? Color { get; set; }

    /// <summary>
    ///     The accessible name. Set it when the icon stands alone; leave it unset when adjacent text
    ///     already says the same thing, so it is not announced twice.
    /// </summary>
    public string? AriaLabel { get; set; }

    private static readonly IReadOnlyDictionary<string, string?> Hidden =
        new Dictionary<string, string?> { ["hidden"] = "true" };

    protected override Component? Render()
    {
        var cls = BsClass.Join("bi", $"bi-{Name.ToCssName()}", Color is { } c ? c.Text() : null, Class);

        return AriaLabel is { } label
            ? I
                .Id(Id)
                .Class(cls)
                .Role("img")
                .Aria(new Dictionary<string, string?> { ["label"] = label })[Items]
            : I.Id(Id).Class(cls).Aria(Hidden)[Items];
    }
}
