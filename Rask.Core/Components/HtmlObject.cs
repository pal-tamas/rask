using System.Globalization;

namespace Rask.Core.Components;

// Renders the <object> HTML tag. Renamed from Object to avoid shadowing System.Object.
public sealed class HtmlObject : Component<HtmlObject.Props>
{
    public HtmlObject(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public HtmlObject(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "object";

    public new sealed record Props(
        string? DataUrl = null,
        string? Type = null,
        string? Name = null,
        int? Width = null,
        int? Height = null,
        string? Form = null,
        string? UseMap = null,
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

            if (DataUrl is not null)
            {
                yield return new KeyValuePair<string, string?>("data", DataUrl);
            }

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
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

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }

            if (UseMap is not null)
            {
                yield return new KeyValuePair<string, string?>("usemap", UseMap);
            }
        }
    }
}
