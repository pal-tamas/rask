using System.Text;

namespace Rask.Html.Components;

public sealed partial class Map : Element
{
    protected override string TagName => "map";

    public string? Name { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }
    }
}
