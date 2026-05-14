using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Button : Element
{
    protected override string TagName => "button";

    public string? Type { get; set; }
    public bool Disabled { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public Action? OnClick { get; set; }
    public Func<Task>? OnClickAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Type is not null) yield return new("type", Type);
        if (Disabled) yield return new("disabled", null);
        if (Name is not null) yield return new("name", Name);
        if (Value is not null) yield return new("value", Value);

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null && LiveRenderContext.Current is { } ctx)
        {
            yield return new("data-rask-on-click", ctx.RegisterHandler(click));
        }
    }
}
