namespace Rask.Core.Components;

public sealed class Time : Component<Time.Props>
{
    public Time(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Time(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "time";

    public new sealed record Props(
        string? DateTime = null,
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

            if (DateTime is not null)
            {
                yield return new KeyValuePair<string, string?>("datetime", DateTime);
            }
        }
    }
}
