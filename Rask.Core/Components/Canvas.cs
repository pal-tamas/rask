using System.Globalization;

namespace Rask.Core.Components;

public sealed class Canvas : Component<Canvas.Props>
{
    public Canvas(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Canvas(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "canvas";

    public new sealed record Props(
        int? Width = null,
        int? Height = null,
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
        }
    }
}
