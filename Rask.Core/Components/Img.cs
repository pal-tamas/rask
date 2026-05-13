using System.Globalization;

namespace Rask.Core.Components;

public sealed class Img : Component
{
    protected override string TagName => "img";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Alt { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Loading { get; set; }
    public string? Srcset { get; set; }
    public string? Sizes { get; set; }
    public string? CrossOrigin { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Decoding { get; set; }
    public string? UseMap { get; set; }
    public bool Ismap { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Src is not null) yield return new("src", Src);
        if (Alt is not null) yield return new("alt", Alt);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));
        if (Loading is not null) yield return new("loading", Loading);
        if (Srcset is not null) yield return new("srcset", Srcset);
        if (Sizes is not null) yield return new("sizes", Sizes);
        if (CrossOrigin is not null) yield return new("crossorigin", CrossOrigin);
        if (ReferrerPolicy is not null) yield return new("referrerpolicy", ReferrerPolicy);
        if (Decoding is not null) yield return new("decoding", Decoding);
        if (UseMap is not null) yield return new("usemap", UseMap);
        if (Ismap) yield return new("ismap", null);
    }
}
