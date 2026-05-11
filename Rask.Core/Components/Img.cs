using System.Globalization;

namespace Rask.Core.Components;

public sealed class Img : Component<Img.Props>
{
    public Img(Props? props = null) : base(props, null) { }

    protected override string TagName => "img";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Src = null,
        string? Alt = null,
        int? Width = null,
        int? Height = null,
        string? Loading = null,
        string? Srcset = null,
        string? Sizes = null,
        string? CrossOrigin = null,
        string? ReferrerPolicy = null,
        string? Decoding = null,
        string? UseMap = null,
        bool Ismap = false,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data)
    {
        public override IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            foreach (var kv in base.ToAttributes())
            {
                yield return kv;
            }

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Alt is not null)
            {
                yield return new KeyValuePair<string, string?>("alt", Alt);
            }

            if (Width is not null)
            {
                yield return new KeyValuePair<string, string?>("width",
                    Width.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Height is not null)
            {
                yield return new KeyValuePair<string, string?>("height",
                    Height.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Loading is not null)
            {
                yield return new KeyValuePair<string, string?>("loading", Loading);
            }

            if (Srcset is not null)
            {
                yield return new KeyValuePair<string, string?>("srcset", Srcset);
            }

            if (Sizes is not null)
            {
                yield return new KeyValuePair<string, string?>("sizes", Sizes);
            }

            if (CrossOrigin is not null)
            {
                yield return new KeyValuePair<string, string?>("crossorigin", CrossOrigin);
            }

            if (ReferrerPolicy is not null)
            {
                yield return new KeyValuePair<string, string?>("referrerpolicy", ReferrerPolicy);
            }

            if (Decoding is not null)
            {
                yield return new KeyValuePair<string, string?>("decoding", Decoding);
            }

            if (UseMap is not null)
            {
                yield return new KeyValuePair<string, string?>("usemap", UseMap);
            }

            if (Ismap)
            {
                yield return new KeyValuePair<string, string?>("ismap", null);
            }
        }
    }
}
