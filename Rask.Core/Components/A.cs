using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class A : Element
{
    protected override string TagName => "a";

    public string? Href { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public string? Download { get; set; }
    public string? Hreflang { get; set; }
    public string? Type { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Ping { get; set; }
    public Action? OnClick { get; set; }
    public Func<Task>? OnClickAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Href is not null) yield return new("href", Href);
        if (Target is not null) yield return new("target", Target);
        if (Rel is not null) yield return new("rel", Rel);
        if (Download is not null) yield return new("download", Download);
        if (Hreflang is not null) yield return new("hreflang", Hreflang);
        if (Type is not null) yield return new("type", Type);
        if (ReferrerPolicy is not null) yield return new("referrerpolicy", ReferrerPolicy);
        if (Ping is not null) yield return new("ping", Ping);

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null && LiveRenderContext.Current is { } ctx)
        {
            yield return new("data-rask-on-click", ctx.RegisterHandler(click));
        }
    }
}
