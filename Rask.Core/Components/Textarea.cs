using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;
using C = Rask.Core.Components.Components;

namespace Rask.Core.Components;

public sealed class Textarea : Element
{
    // Expression-driven factory; see Input.Bound for the broader pattern. Textarea always
    // updates per-keystroke (OnInput) since textareas are inherently string-valued.
    // `Validate` ships as three overloads (none / typed sync / typed async) so callers can
    // pass a bare lambda without the `(Func<…>)` cast.
    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => BoundCore(Bind, Name, Rows, Cols, Placeholder, Required, Disabled, ReadOnly,
            MaxLength, MinLength, Wrap, Autofocus, Autocomplete,
            validate: null, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, IEnumerable<string>> Validate,
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => BoundCore(Bind, Name, Rows, Cols, Placeholder, Required, Disabled, ReadOnly,
            MaxLength, MinLength, Wrap, Autofocus, Autocomplete,
            validate: Validate, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, CancellationToken, ValueTask<IEnumerable<string>>> Validate,
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        => BoundCore(Bind, Name, Rows, Cols, Placeholder, Required, Disabled, ReadOnly,
            MaxLength, MinLength, Wrap, Autofocus, Autocomplete,
            validate: Validate, Id, Class, Style, Data);

    private static Textarea BoundCore<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name,
        int? Rows,
        int? Cols,
        string? Placeholder,
        bool Required,
        bool Disabled,
        bool ReadOnly,
        int? MaxLength,
        int? MinLength,
        string? Wrap,
        bool Autofocus,
        string? Autocomplete,
        Delegate? validate,
        string? Id,
        string? Class,
        string? Style,
        IReadOnlyDictionary<string, string?>? Data)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        ctx?.RegisterFieldValidator(fid, validate, () => acc.Getter());
        var stringValue = BindingHelpers.FormatValue(acc.Getter());

        return (Textarea)C.Textarea(
            Name: name, Rows: Rows, Cols: Cols, Placeholder: Placeholder,
            Required: Required, Disabled: Disabled, ReadOnly: ReadOnly,
            MaxLength: MaxLength, MinLength: MinLength, Wrap: Wrap,
            Autofocus: Autofocus, Autocomplete: Autocomplete,
            OnInputAsync: BindingHelpers.StringSetHandler(acc, ctx, fid, false),
            OnChangeAsync: BindingHelpers.TouchAndValidateHandler(acc, ctx, fid, false),
            Id: Id, Class: Class, Style: Style, Data: Data)[stringValue];
    }

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
