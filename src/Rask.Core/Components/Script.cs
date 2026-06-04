using System.Text;

namespace Rask.Core.Components;

public sealed class Script : Element
{
    protected override string TagName => "script";

    public string? Src { get; set; }
    public string? Type { get; set; }
    public bool? Async { get; set; }
    public bool? Defer { get; set; }
    public string? CrossOrigin { get; set; }
    public string? Integrity { get; set; }
    public bool? NoModule { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Charset { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Async is true)
        {
            AppendAttr(sb, "async", null);
        }

        if (Defer is true)
        {
            AppendAttr(sb, "defer", null);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        if (Integrity is not null)
        {
            AppendAttr(sb, "integrity", Integrity);
        }

        if (NoModule is true)
        {
            AppendAttr(sb, "nomodule", null);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Charset is not null)
        {
            AppendAttr(sb, "charset", Charset);
        }
    }
}
