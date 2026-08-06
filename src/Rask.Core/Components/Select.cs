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

    // Non-null only for a multi-select bound to a collection, where the render has to mark every picked
    // option rather than the one _selectedValue names.
    private IReadOnlySet<string>? _selectedValues;

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
            Children = MarkSelected(Children, new Selection(_selectedValue, _selectedValues));
        }

        return base.EnterChildrenScope();
    }

    private static IEnumerable<Component?> MarkSelected(IEnumerable<Component?> children, Selection current)
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

    // Which option values the render should mark. A single-value select carries just the formatted
    // value; a multi-select bound to a collection carries the set. A struct with a null set keeps the
    // single-value path — by far the common one — allocation-free.
    private readonly struct Selection(string single, IReadOnlySet<string>? many)
    {
        public bool Matches(string? value) =>
            many is null ? value == single : value is not null && many.Contains(value);
    }

    private static Option MarkOption(Option opt, Selection current)
    {
        if (opt.Selected is true || !current.Matches(opt.Value))
        {
            return opt;
        }

        return new Option
        {
            // Preserve Key: the marked option must keep the same reconciliation identity as the original,
            // otherwise the selected option's key shifts every render (the marked one loses its key while
            // the previously-marked one regains it). Keyed reconciliation then mismatches and the browser's
            // live `selected` IDL property is never synced — the <select> visually snaps back to the old
            // value even though the `selected` attribute is written to the right option.
            Key = opt.Key,
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

    private static Optgroup MarkOptgroup(Optgroup og, Selection current)
    {
        if (og.Children is null)
        {
            return og;
        }

        var newChildren = og.Children.Select(c =>
            c is Option o ? MarkOption(o, current) : c).ToArray();
        return new Optgroup
        {
            // Preserve Key for stable reconciliation identity (see MarkOption).
            Key = og.Key,
            Disabled = og.Disabled,
            Label = og.Label,
            Id = og.Id,
            Class = og.Class,
            Style = og.Style,
            Data = og.Data,
            Children = newChildren
        };
    }

    // The picked values a multi-select bound to a collection should mark, or null for every other shape —
    // which keeps the single-value path on its existing string compare.
    private IReadOnlySet<string>? SelectionSet(object? bound)
    {
        if (Multiple is not true || !BindingHelpers.IsBindableSelectionType<T>())
        {
            return null;
        }

        var picked = new HashSet<string>(StringComparer.Ordinal);
        if (bound is IEnumerable<string> values)
        {
            foreach (var v in values)
            {
                picked.Add(v);
            }
        }

        return picked;
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
            _selectedValues = SelectionSet(acc.Getter());
        }
        else if (Value is not null)
        {
            _bound = true;
            _selectedValue = BindingHelpers.FormatValue(Value);
            _selectedValues = SelectionSet(Value);
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
            // A multi-select bound to a collection takes the whole selection the client now reports
            // (`values`), not the single `value` the DOM exposes — which is only the FIRST selected
            // option, so binding it converged the model on one option out of however many were picked.
            // A multi-select bound to a scalar keeps the single-value handler: that is a control whose
            // model can hold one answer, and silently widening it would be the more surprising change.
            var handler = Multiple is true && BindingHelpers.IsBindableSelectionType<T>()
                ? BindingHelpers.MultiSelectSetHandler<T>(acc, bindCtx, fid, afterBind)
                : (Delegate)BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, true, afterBind);
            AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(handler));
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
