namespace Rask.Bootstrap;

// A spacing wrapper for one form field (Bootstrap's .mb-3 convention) — group a label, control and
// help text when you compose them by hand instead of using a BsInput/BsSelect's built-in layout.
public sealed class BsFormGroup : BsBlock
{
    protected override RenderResult Render() => Div(Id: Id, Class: BsClass.Join("mb-3", Class))[Items];
}

// A Bootstrap form label: <label class="form-label">. Set For to tie it to a control id.
public sealed class BsFormLabel : BsBlock
{
    public string? For { get; set; }

    protected override RenderResult Render() =>
        Rask.Core.Components.Generated.Label(For: For, Id: Id, Class: BsClass.Join("form-label", Class))[Items];
}
