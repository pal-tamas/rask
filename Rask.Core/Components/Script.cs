namespace Rask.Core.Components;

public sealed class Script : Component<Script.Props>
{
    public Script(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Script(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "script";

    public new sealed record Props(
        string? Src = null,
        string? Type = null,
        bool Async = false,
        bool Defer = false,
        string? CrossOrigin = null,
        string? Integrity = null,
        bool NoModule = false,
        string? ReferrerPolicy = null,
        string? Charset = null,
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

            if (Async)
            {
                yield return new KeyValuePair<string, string?>("async", null);
            }

            if (Defer)
            {
                yield return new KeyValuePair<string, string?>("defer", null);
            }

            if (CrossOrigin is not null)
            {
                yield return new KeyValuePair<string, string?>("crossorigin", CrossOrigin);
            }

            if (Integrity is not null)
            {
                yield return new KeyValuePair<string, string?>("integrity", Integrity);
            }

            if (NoModule)
            {
                yield return new KeyValuePair<string, string?>("nomodule", null);
            }

            if (ReferrerPolicy is not null)
            {
                yield return new KeyValuePair<string, string?>("referrerpolicy", ReferrerPolicy);
            }

            if (Charset is not null)
            {
                yield return new KeyValuePair<string, string?>("charset", Charset);
            }
        }
    }
}
