namespace Rask.Core.Components;

public sealed class Details : Component<Details.Props>
{
    public Details(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Details(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "details";

    public new sealed record Props(
        bool Open = false,
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

            if (Open)
            {
                yield return new KeyValuePair<string, string?>("open", null);
            }
        }
    }
}
