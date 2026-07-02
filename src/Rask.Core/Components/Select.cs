using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// Generic <select> form control implementing IFormControl<T>. The generator synthesizes a controlled/plain
// factory and a Bind-first bound factory (validator fanned none/sync/async). Binding is resolved at render
// time (WriteAttributes); the matching <option> is pre-marked selected just before the serializer reads
// Children (EnterChildrenScope), so the initial render reflects the bound/controlled value without a
// round-trip. Plain usage stays `Select<string>(Name: …)[Option(…)…]`; bound infers T from the expression.
public sealed class Select<T> : Element, IFormControl<T>
{
    // Set in WriteAttributes (bound/controlled); a plain select leaves _bound false and skips marking.
    private bool _bound;
    private string _selectedValue = "";

    protected override string TagName => "select";

    public string? Name { get; set; }
    public bool? Multiple { get; set; }
    public bool? Required { get; set; }
    public bool? Disabled { get; set; }
    public int? Size { get; set; }
    public string? Form { get; set; }
    public bool? Autofocus { get; set; }
    public string? Autocomplete { get; set; }

    // IFormControl<T> — controlled mode (OnChange/OnChangeAsync are the typed change callbacks).
    public Callback<T>? OnChange { get; set; }
    public CallbackAsync<T>? OnChangeAsync { get; set; }
    public T? Value { get; set; }

    // IFormControl<T> — bound mode (excluded from the controlled factory by the generator).
    public Expression<Func<T>>? Bind { get; set; }
    public Validate<T>? Validate { get; set; }
    public ValidateAsync<T>? ValidateAsync { get; set; }
    public Action<T>? AfterBind { get; set; }
    public Func<T, Task>? AfterBindAsync { get; set; }

    protected override IDisposable? EnterChildrenScope()
    {
        if (_bound && Children is not null)
        {
            Children = MarkSelected(Children, _selectedValue);
        }

        return base.EnterChildrenScope();
    }

    private static IEnumerable<Component?> MarkSelected(IEnumerable<Component?> children, string current)
    {
        var list = new List<Component?>();
        foreach (var c in children)
        {
            if (c is Option opt)
            {
                list.Add(MarkOption(opt, current));
            }
            else if (c is Optgroup og)
            {
                list.Add(MarkOptgroup(og, current));
            }
            else
            {
                list.Add(c);
            }
        }

        // Return an array so Children stays a Component?[] and the serializer's zero-allocation
        // fast path (ChildrenArray => Children as Component?[]) still applies after marking.
        return list.ToArray();
    }

    private static Option MarkOption(Option opt, string current)
    {
        if (opt.Selected is true || opt.Value != current)
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
            c is Option o ? MarkOption(o, current) : c).ToArray();
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

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        ExpressionAccessor.Accessor? acc = null;
        EditContext? bindCtx = null;
        var fid = default(FieldIdentifier);
        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            bindCtx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            _bound = true;
            _selectedValue = BindingHelpers.FormatValue(acc.Getter());
        }
        else if (Value is not null)
        {
            _bound = true;
            _selectedValue = BindingHelpers.FormatValue(Value);
        }

        var name = Name ?? acc?.PropertyName;
        if (name is not null)
        {
            AppendAttr(sb, "name", name);
        }

        if (Multiple is true)
        {
            AppendAttr(sb, "multiple", null);
        }

        if (Required is true)
        {
            AppendAttr(sb, "required", null);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Size is not null)
        {
            AppendAttr(sb, "size", Size.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind, AfterBindAsync);
            ((IFormControl<T>)this).RegisterValidator(acc, bindCtx);
            AppendAttr(sb, "data-rask-on-change",
                ctx.RegisterHandler(BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, true, afterBind)));
        }
        else
        {
            var change = ((IFormControl<T>)this).ControlledChangeHandler();
            if (change is not null)
            {
                AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(change));
            }
        }
    }
}
