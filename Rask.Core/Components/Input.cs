using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Input : Component
{
    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Disabled { get; set; }
    public bool ReadOnly { get; set; }
    public bool Checked { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Step { get; set; }
    public string? Pattern { get; set; }
    public int? Size { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool Multiple { get; set; }
    public string? Accept { get; set; }
    public string? Alt { get; set; }
    public string? Autocomplete { get; set; }
    public bool Autofocus { get; set; }
    public string? Form { get; set; }
    public string? FormAction { get; set; }
    public string? FormEnctype { get; set; }
    public string? FormMethod { get; set; }
    public bool FormNovalidate { get; set; }
    public string? FormTarget { get; set; }
    public string? List { get; set; }
    public string? Src { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public Action<string>? OnInput { get; set; }
    public Action<string>? OnChange { get; set; }
    public Func<string, Task>? OnInputAsync { get; set; }
    public Func<string, Task>? OnChangeAsync { get; set; }

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Type is not null) yield return new("type", Type);
        if (Name is not null) yield return new("name", Name);
        if (Value is not null) yield return new("value", Value);
        if (Placeholder is not null) yield return new("placeholder", Placeholder);
        if (Required) yield return new("required", null);
        if (Disabled) yield return new("disabled", null);
        if (ReadOnly) yield return new("readonly", null);
        if (Checked) yield return new("checked", null);
        if (Min is not null) yield return new("min", Min);
        if (Max is not null) yield return new("max", Max);
        if (Step is not null) yield return new("step", Step);
        if (Pattern is not null) yield return new("pattern", Pattern);
        if (Size is not null) yield return new("size", Size.Value.ToString(CultureInfo.InvariantCulture));
        if (MaxLength is not null) yield return new("maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        if (MinLength is not null) yield return new("minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        if (Multiple) yield return new("multiple", null);
        if (Accept is not null) yield return new("accept", Accept);
        if (Alt is not null) yield return new("alt", Alt);
        if (Autocomplete is not null) yield return new("autocomplete", Autocomplete);
        if (Autofocus) yield return new("autofocus", null);
        if (Form is not null) yield return new("form", Form);
        if (FormAction is not null) yield return new("formaction", FormAction);
        if (FormEnctype is not null) yield return new("formenctype", FormEnctype);
        if (FormMethod is not null) yield return new("formmethod", FormMethod);
        if (FormNovalidate) yield return new("formnovalidate", null);
        if (FormTarget is not null) yield return new("formtarget", FormTarget);
        if (List is not null) yield return new("list", List);
        if (Src is not null) yield return new("src", Src);
        if (Width is not null) yield return new("width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) yield return new("height", Height.Value.ToString(CultureInfo.InvariantCulture));

        if (LiveRenderContext.Current is { } ctx)
        {
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null) yield return new("data-rask-on-input", ctx.RegisterHandler(input));

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null) yield return new("data-rask-on-change", ctx.RegisterHandler(change));
        }
    }
}
