namespace Rask.Core.Components;

public sealed class Base : Component<Base.Props>
{
    public Base(Props? props = null) : base(props, null) { }

    protected override string TagName => "base";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Href = null,
        string? Target = null,
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

            if (Href is not null)
            {
                yield return new KeyValuePair<string, string?>("href", Href);
            }

            if (Target is not null)
            {
                yield return new KeyValuePair<string, string?>("target", Target);
            }
        }
    }
}
