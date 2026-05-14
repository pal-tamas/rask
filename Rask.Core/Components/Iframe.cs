using System.Globalization;

namespace Rask.Core.Components;

public sealed class Iframe : Element
{
    protected override string TagName => "iframe";

    public string? Src { get; set; }
    public string? Srcdoc { get; set; }
    public string? Name { get; set; }
    public string? Sandbox { get; set; }
    public string? Allow { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Loading { get; set; }
    public string? ReferrerPolicy { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Srcdoc is not null) yield return new("srcdoc", Srcdoc);
        if (Name is not null) yield return new("name", Name);
        if (Sandbox is not null) yield return new("sandbox", Sandbox);
        if (Allow is not null) yield return new("allow", Allow);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
        if (Loading is not null) yield return new("loading", Loading);
        if (ReferrerPolicy is not null) yield return new("referrerpolicy", ReferrerPolicy);
    }
}
