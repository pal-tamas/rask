using System.Globalization;

namespace Rask.Core.Components;

public sealed class Canvas : Component
{
    protected override string TagName => "canvas";

    public int? Width { get; set; }
    public int? Height { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
    }
}
