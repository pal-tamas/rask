namespace Rask.Core.Components;

public sealed class Bdo : Component<Bdo.Props>
{
    public Bdo(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Bdo(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "bdo";

    public new sealed record Props(
        string? Dir = null,
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

            if (Dir is not null)
            {
                yield return new KeyValuePair<string, string?>("dir", Dir);
            }
        }
    }
}
