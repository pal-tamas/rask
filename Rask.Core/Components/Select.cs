using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;
using Rask.Core.Live;
using C = Rask.Core.Components.Components;

namespace Rask.Core.Components;

public sealed class Select : Element
{
    // Expression-driven factory; pre-marks the matching <option> as selected so the
    // initial render reflects the bound value without round-tripping through the browser.
    // `Validate` ships as three overloads to avoid the `(Func<…>)` call-site cast — see
    // Input.Bound for the dispatch rationale.
    [GenerateForwarderFactory]
    public static Select Bound<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name = null,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        params IEnumerable<Child> Children)
        => BoundCore(Bind, Name, Required, Disabled, Size, Autofocus, Autocomplete,
            validate: null, Id, Class, Style, Data, Children);

    [GenerateForwarderFactory]
    public static Select Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, IEnumerable<string>> Validate,
        string? Name = null,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        params IEnumerable<Child> Children)
        => BoundCore(Bind, Name, Required, Disabled, Size, Autofocus, Autocomplete,
            validate: Validate, Id, Class, Style, Data, Children);

    [GenerateForwarderFactory]
    public static Select Bound<TProp>(
        Expression<Func<TProp>> Bind,
        Func<TProp, CancellationToken, ValueTask<IEnumerable<string>>> Validate,
        string? Name = null,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        params IEnumerable<Child> Children)
        => BoundCore(Bind, Name, Required, Disabled, Size, Autofocus, Autocomplete,
            validate: Validate, Id, Class, Style, Data, Children);

    private static Select BoundCore<TProp>(
        Expression<Func<TProp>> Bind,
        string? Name,
        bool Required,
        bool Disabled,
        int? Size,
        bool Autofocus,
        string? Autocomplete,
        Delegate? validate,
        string? Id,
        string? Class,
        string? Style,
        IReadOnlyDictionary<string, string?>? Data,
        IEnumerable<Child> Children)
    {
        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        var fid = acc.Field;
        var name = Name ?? acc.PropertyName;
        ctx?.RegisterFieldValidator(fid, validate, () => acc.Getter());
        var current = BindingHelpers.FormatValue(acc.Getter());
        var preselected = MarkSelected(Children, current);

        return (Select)C.Select(
            Name: name, Required: Required, Disabled: Disabled, Size: Size,
            Autofocus: Autofocus, Autocomplete: Autocomplete,
            OnChangeAsync: BindingHelpers.TouchAndValidateHandler(acc, ctx, fid, true),
            Id: Id, Class: Class, Style: Style, Data: Data)[preselected];
    }

    private static IEnumerable<Child> MarkSelected(IEnumerable<Child> children, string current)
    {
        var list = new List<Child>();
        foreach (var c in children)
        {
            if (c.Component is Option opt)
            {
                list.Add(MarkOption(opt, current));
            }
            else if (c.Component is Optgroup og)
            {
                list.Add(MarkOptgroup(og, current));
            }
            else
            {
                list.Add(c);
            }
        }

        return list;
    }

    private static Option MarkOption(Option opt, string current)
    {
        if (opt.Selected || opt.Value != current)
        {
            return opt;
        }

        return new Option
        {
            Value = opt.Value,
            Selected = true,
            Disabled = opt.Disabled,
            Label = opt.Label,
            Id = opt.Id,
            Class = opt.Class,
            Style = opt.Style,
            Data = opt.Data,
            Children = opt.Children
        };
    }

    private static Optgroup MarkOptgroup(Optgroup og, string current)
    {
        if (og.Children is null)
        {
            return og;
        }

        var newChildren = og.Children.Select(c =>
            c.Component is Option o ? (Child)MarkOption(o, current) : c).ToArray();
        return new Optgroup
        {
            Disabled = og.Disabled,
            Label = og.Label,
            Id = og.Id,
            Class = og.Class,
            Style = og.Style,
            Data = og.Data,
            Children = newChildren
        };
    }

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
