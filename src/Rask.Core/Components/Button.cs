using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Button : Element
{
    protected override string TagName => "button";

    public string? Type { get; set; }
    public bool? Disabled { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public Callback? OnClick { get; set; }
    public CallbackAsync? OnClickAsync { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null && LiveRenderContext.CurrentSync is { } ctx)
        {
            AppendAttr(sb, "data-rask-on-click", ctx.RegisterHandler(click));
        }
    }
}
