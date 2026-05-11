namespace Rask.Core.Components;

public sealed class Map : Component<Map.Props>
{
    public Map(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Map(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "map";

    public new sealed record Props(
        string? Name = null,
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

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }
        }
    }
}
