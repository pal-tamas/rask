using System.Globalization;

namespace Rask.Core.Components;

public sealed class Embed : Component<Embed.Props>
{
    public Embed(Props? props = null) : base(props, null) { }

    protected override string TagName => "embed";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Src = null,
        string? Type = null,
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

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
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
