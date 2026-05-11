namespace Rask.Core.Components;

public sealed class Data : Component<Data.Props>
{
    public Data(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Data(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "data";

    public new sealed record Props(
        string? Value = null,
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

            if (Value is not null)
            {
                yield return new KeyValuePair<string, string?>("value", Value);
            }
        }
    }
}
