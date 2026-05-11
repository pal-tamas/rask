using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class A : Component<A.Props>
{
    public A(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public A(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "a";

    public new sealed record Props(
        string? Href = null,
        string? Target = null,
        string? Rel = null,
        string? Download = null,
        string? Hreflang = null,
        string? Type = null,
        string? ReferrerPolicy = null,
        string? Ping = null,
        Action? OnClick = null,
        Func<Task>? OnClickAsync = null,
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

            if (Rel is not null)
            {
                yield return new KeyValuePair<string, string?>("rel", Rel);
            }

            if (Download is not null)
            {
                yield return new KeyValuePair<string, string?>("download", Download);
            }

            if (Hreflang is not null)
            {
                yield return new KeyValuePair<string, string?>("hreflang", Hreflang);
            }

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (ReferrerPolicy is not null)
            {
                yield return new KeyValuePair<string, string?>("referrerpolicy", ReferrerPolicy);
            }

            if (Ping is not null)
            {
                yield return new KeyValuePair<string, string?>("ping", Ping);
            }

            var click = (Delegate?)OnClick ?? OnClickAsync;
            if (click is not null && LiveRenderContext.Current is { } ctx)
            {
                yield return new KeyValuePair<string, string?>("data-rask-on-click", ctx.RegisterHandler(click));
            }
        }
    }
}
