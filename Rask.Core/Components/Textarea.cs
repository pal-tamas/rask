using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Textarea : Component
{
    protected override string TagName => "textarea";

    public string? Name { get; set; }
    public int? Rows { get; set; }
    public int? Cols { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public bool ReadOnly { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public string? Wrap { get; set; }
    public bool Autofocus { get; set; }
    public string? Autocomplete { get; set; }
    public string? Form { get; set; }
    public string? Dirname { get; set; }
    public Action<string>? OnInput { get; set; }
    public Action<string>? OnChange { get; set; }
    public Func<string, Task>? OnInputAsync { get; set; }
    public Func<string, Task>? OnChangeAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Name is not null) yield return new("name", Name);
        if (Rows is not null) yield return new("rows", Rows.Value.ToString(CultureInfo.InvariantCulture));
        if (Cols is not null) yield return new("cols", Cols.Value.ToString(CultureInfo.InvariantCulture));
        if (Placeholder is not null) yield return new("placeholder", Placeholder);
        if (Required) yield return new("required", null);
        if (Disabled) yield return new("disabled", null);
        if (ReadOnly) yield return new("readonly", null);
        if (MaxLength is not null) yield return new("maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        if (MinLength is not null) yield return new("minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        if (Wrap is not null) yield return new("wrap", Wrap);
        if (Autofocus) yield return new("autofocus", null);
        if (Autocomplete is not null) yield return new("autocomplete", Autocomplete);
        if (Form is not null) yield return new("form", Form);
        if (Dirname is not null) yield return new("dirname", Dirname);

        if (LiveRenderContext.Current is { } ctx)
        {
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null) yield return new("data-rask-on-input", ctx.RegisterHandler(input));

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null) yield return new("data-rask-on-change", ctx.RegisterHandler(change));
        }
    }
}
