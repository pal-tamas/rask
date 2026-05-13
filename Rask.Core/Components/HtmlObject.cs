using System.Globalization;

namespace Rask.Core.Components;

// Renders the <object> HTML tag. Renamed from Object to avoid shadowing System.Object.
public sealed class HtmlObject : Component
{
    protected override string TagName => "object";

    public string? DataUrl { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Form { get; set; }
    public string? UseMap { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (DataUrl is not null) yield return new("data", DataUrl);
        if (Type is not null) yield return new("type", Type);
        if (Name is not null) yield return new("name", Name);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
        if (Form is not null) yield return new("form", Form);
        if (UseMap is not null) yield return new("usemap", UseMap);
    }
}
