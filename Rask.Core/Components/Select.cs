using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Select : Component
{
    protected override string TagName => "select";

    public string? Name { get; set; }
    public bool Multiple { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public int? Size { get; set; }
    public string? Form { get; set; }
    public bool Autofocus { get; set; }
    public string? Autocomplete { get; set; }
    public Action<string>? OnChange { get; set; }
    public Func<string, Task>? OnChangeAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Name is not null) yield return new("name", Name);
        if (Multiple) yield return new("multiple", null);
        if (Required) yield return new("required", null);
        if (Disabled) yield return new("disabled", null);
        if (Size is not null) yield return new("size", Size.Value.ToString(CultureInfo.InvariantCulture));
        if (Form is not null) yield return new("form", Form);
        if (Autofocus) yield return new("autofocus", null);
        if (Autocomplete is not null) yield return new("autocomplete", Autocomplete);

        var change = (Delegate?)OnChange ?? OnChangeAsync;
        if (change is not null && LiveRenderContext.Current is { } ctx)
        {
            yield return new("data-rask-on-change", ctx.RegisterHandler(change));
        }
    }
}
