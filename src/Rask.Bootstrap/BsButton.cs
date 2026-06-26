using System.Text;

namespace Rask.Bootstrap;

// A Bootstrap button: <button class="btn btn-{color} btn-{size}">. Extends Element so the factory
// exposes every universal HTML attribute (Id/Class/Style/Data/Aria/Ref) and the full event surface
// (OnClick/OnClickAsync, …) for free; the typed Color/Outline/Size/Active props compose the Bootstrap
// classes through ResolveClass. For a link styled as a button, set Class:"btn btn-link" on an A().
public sealed class BsButton : Element
{
    protected override string TagName => "button";

    // Theme color. Null renders a class-less .btn (use Class to style), matching Bootstrap.
    public BsColor? Color { get; set; }

    // Outline variant (btn-outline-{color}); ignored when Color is null.
    public bool? Outline { get; set; }

    public BsSize? Size { get; set; }

    // Toggle/pressed state: adds .active and aria-pressed="true".
    public bool? Active { get; set; }

    // Defaults to "button" so a BsButton never implicitly submits an enclosing form; pass
    // Type:"submit" explicitly for a submit button.
    public string? Type { get; set; }

    public bool? Disabled { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }

    protected override string? ResolveClass() => BsClass.Join(
        "btn",
        Color is { } c ? c.Button(Outline is true) : null,
        Size is { } s ? s.ButtonSize() : null,
        Active is true ? "active" : null,
        Class);

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        AppendAttr(sb, "type", Type ?? "button");

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Active is true)
        {
            AppendAttr(sb, "aria-pressed", "true");
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }
    }
}
