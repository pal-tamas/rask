namespace Rask.Bootstrap;

// A Bootstrap icon: <i class="bi bi-{name}">. Name is a typed enum of every Bootstrap Icons glyph, so
// icon names are discoverable and compile-checked (no string typos). Decorative by default
// (aria-hidden="true"); set AriaLabel to expose the icon to assistive tech as an image.
public sealed partial class BsIcon : BsBlock
{
    public BsIconName Name { get; set; }
    public BsColor? Color { get; set; }
    public string? AriaLabel { get; set; }

    private static readonly IReadOnlyDictionary<string, string?> Hidden =
        new Dictionary<string, string?> { ["hidden"] = "true" };

    protected override Component? Render()
    {
        var cls = BsClass.Join("bi", $"bi-{Name.ToCssName()}", Color is { } c ? c.Text() : null, Class);

        return AriaLabel is { } label
            ? I(Id: Id, Class: cls, Role: "img",
                Aria: new Dictionary<string, string?> { ["label"] = label })[Items]
            : I(Id: Id, Class: cls, Aria: Hidden)[Items];
    }
}
