using System.Globalization;

namespace Rask.Core.Components;

public sealed class Embed : Component
{
    protected override string TagName => "embed";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Type { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Type is not null) yield return new("type", Type);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
    }
}
