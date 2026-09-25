using System.Linq.Expressions;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// The typed layer over MDN's HTMLTextAreaElement (generated, abstract, every plain attribute). The bound value
// type T is usually string; non-string T round-trips through FormatValue (T→string) and the binding parser
// (string→T). The textarea's text content is the value, emitted as a child text node by RenderChildren.

/// <summary>
///     A multi-line text field typed by the value it edits. Unlike <c>input</c>, its value is its text content,
///     and it is resizable by default — <c>Rows</c> and <c>Cols</c> only set the initial size.
/// </summary>
public sealed partial class HTMLTextAreaElement<T> : HTMLTextAreaElement, IFormControl<T>
{
    /// <summary>The name submitted with the form. A bound textarea defaults it to the bound member's name.</summary>
    public string? Name { get; set; }














    // Per-keystroke DOM handler (a textarea is inherently string-valued); not part of IFormControl, but
    // recognised as a controlled-mode member by name all the same.
    /// <summary>
    ///     Called on every keystroke with the current text — the hook for a character counter or an
    ///     autosizing textarea. It fires mid-word, so debounce anything that costs more than a render.
    /// </summary>
    /// <remarks>
    ///     Controlled mode only: a bound control installs its own <c>oninput</c> write-back and never
    ///     reads this, so it is neither a step on a bound chain nor a parameter of the bound factory. Use
    ///     <see cref="AfterBind" /> for a side effect on each bound write.
    /// </remarks>
    public Callback<string>? OnInput { get; set; }


    // IFormControl<T> — bound mode.

    /// <summary>
    ///     The model field this control is bound to, as an expression such as <c>() => model.Notes</c>.
    /// </summary>
    public Expression<Func<T>>? Bind { get; set; }

    /// <summary>A check run on the bound value, synchronous or asynchronous.</summary>
    public Validator<T>? Validate { get; set; }


    /// <summary>Runs after a successful bind, once the model has the new value.</summary>
    public Callback<T>? AfterBind { get; set; }


    // IFormControl<T> — controlled mode.

    /// <summary>The control's current value. Prefer <c>Bind</c>.</summary>
    public T? Value { get; set; }

    /// <summary>Called with the new value when the user changes the control, in controlled mode.</summary>
    public Callback<T>? OnChange { get; set; }

    // The rendered text content, resolved in WriteAttributes (bound/controlled) and emitted by
    // RenderChildren. Null leaves the plain Children content (indexer) in place.
    private string? _content;

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        // Bound mode parses the expression up front so the auto-derived `name` lands in attribute order.
        ExpressionAccessor.Accessor? acc = null;
        EditContext? bindCtx = null;
        var fid = default(FieldIdentifier);
        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            bindCtx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            _content = BindingHelpers.FormatValue(acc.Getter());
        }
        else if (Value is not null)
        {
            _content = BindingHelpers.FormatValue(Value);
        }

        var name = Name ?? acc?.PropertyName;
        if (name is not null)
        {
            AppendAttr(sb, "name", name);
        }

        if (LiveRenderContext.CurrentSync is not { } ctx)
        {
            return;
        }

        if (acc is not null)
        {
            // Bound: write the model on input, touch + revalidate on change.
            var afterBind = BindingHelpers.BuildAfterBind(acc, AfterBind);
            ((IFormControl<T>)this).RegisterValidator(acc, bindCtx);
            AppendAttr(sb, "data-rask-on-input",
                ctx.RegisterHandler(BindingHelpers.StringSetHandler(acc, bindCtx, fid, false, afterBind)));
            AppendAttr(sb, "data-rask-on-change",
                ctx.RegisterHandler(BindingHelpers.TouchAndValidateHandler(acc, bindCtx, fid, false)));
            return;
        }

        // Plain / controlled.
        var input = OnInput?.Handler;
        if (input is not null)
        {
            AppendAttr(sb, "data-rask-on-input", ctx.RegisterHandler(input));
        }

        var change = ((IFormControl<T>)this).ControlledChangeHandler();
        if (change is not null)
        {
            AppendAttr(sb, "data-rask-on-change", ctx.RegisterHandler(change));
        }
    }

    protected override IEnumerable<Component?> RenderChildren() =>
        _content is not null ? [_content] : base.RenderChildren();
}
