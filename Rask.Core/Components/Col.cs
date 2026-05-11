using System.Globalization;

namespace Rask.Core.Components;

public sealed class Col : Component<Col.Props>
{
    public Col(Props? props = null) : base(props, null) { }

    protected override string TagName => "col";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        int? Span = null,
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

            if (Span is not null)
            {
                yield return new KeyValuePair<string, string?>("span",
                    Span.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
