using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

namespace Rask.Core.Components;

public sealed class Textarea : Element
{
    protected override string TagName => "textarea";

    public string? Name { get; set; }
    public int? Rows { get; set; }
    public int? Cols { get; set; }
    public string? Placeholder { get; set; }
    public bool? Required { get; set; }
    public bool? Disabled { get; set; }
    public bool? ReadOnly { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public string? Wrap { get; set; }
    public bool? Autofocus { get; set; }
    public string? Autocomplete { get; set; }
    public string? Form { get; set; }
    public string? Dirname { get; set; }
    public Action<string>? OnInput { get; set; }
    public Action<string>? OnChange { get; set; }
    public Func<string, Task>? OnInputAsync { get; set; }

    public Func<string, Task>? OnChangeAsync { get; set; }

    // Expression-driven factory; see Input.Bound for the broader pattern. Textarea always
    // updates per-keystroke (OnInput) since textareas are inherently string-valued.
    // `Validate` ships as three overloads (none / typed sync / typed async) so callers can
    // pass a bare lambda without the `(Func<…>)` cast.
    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
            null, AfterBind, AfterBindAsync, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, IEnumerable<string>> Validate,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
            Validate, AfterBind, AfterBindAsync, Id, Class, Style, Data);

    [GenerateForwarderFactory]
    public static Textarea Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, CancellationToken, ValueTask<IEnumerable<string>>> Validate,
        Action<TProp>? AfterBind = null,
        Func<TProp, Task>? AfterBindAsync = null,
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
            Validate, AfterBind, AfterBindAsync, Id, Class, Style, Data);

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
        Action<TProp>? afterBindSync,
        Func<TProp, Task>? afterBindAsync,
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
        var afterBind = BindingHelpers.BuildAfterBind(acc, afterBindSync, afterBindAsync);
        var stringValue = BindingHelpers.FormatValue(acc.Getter());

        return (Textarea)C.Textarea(
            name, Rows, Cols, Placeholder,
            Required, Disabled, ReadOnly,
            MaxLength, MinLength, Wrap,
            Autofocus, Autocomplete,
            OnInputAsync: BindingHelpers.StringSetHandler(acc, ctx, fid, false, afterBind),
            OnChangeAsync: BindingHelpers.TouchAndValidateHandler(acc, ctx, fid, false),
            Id: Id, Class: Class, Style: Style, Data: Data)[stringValue];
    }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Rows is not null)
        {
            AppendAttr(sb, "rows", Rows.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Cols is not null)
        {
            AppendAttr(sb, "cols", Cols.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Placeholder is not null)
        {
            AppendAttr(sb, "placeholder", Placeholder);
        }

        if (Required is true)
        {
            AppendAttr(sb, "required", null);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (ReadOnly is true)
        {
            AppendAttr(sb, "readonly", null);
        }

        if (MaxLength is not null)
        {
            AppendAttr(sb, "maxlength", MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (MinLength is not null)
        {
            AppendAttr(sb, "minlength", MinLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Wrap is not null)
        {
            AppendAttr(sb, "wrap", Wrap);
        }

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (Dirname is not null)
        {
            AppendAttr(sb, "dirname", Dirname);
        }

        if (LiveRenderContext.Current is { } ctx)
        {
            var input = (Delegate?)OnInput ?? OnInputAsync;
            if (input is not null)
            {
                AppendAttr(sb, "data-rask-on-input", ctx.RegisterHandler(input));
            }

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null)
            {
                AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(change));
            }
        }
    }
}
