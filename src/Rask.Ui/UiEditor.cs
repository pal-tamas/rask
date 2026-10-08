using System.Globalization;
using System.Linq.Expressions;
using Microsoft.JSInterop;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask;

/// <summary>
///     A rich text editor, Flux's <c>flux:editor</c>: a toolbar over an editable area, whose value is HTML.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Editor.Bind(() => _post.Body).Label("Release notes").Description("Explain what's new in this release.")
///     Ui.Editor.Value(_html).OnChange(html => _html = html).Toolbar("heading | bold italic underline | align ~ undo redo")
///     </code>
///     <para>
///     The engine is Tiptap, loaded on first use from <c>/js/rask-ui-editor.js</c> — a file the app's build
///     writes into its <c>wwwroot</c>, and which no page fetches until an editor mounts on it. Until it has
///     loaded, the page shows the value as plain markup.
///     </para>
///     <para>
///     <b>The value is the user's HTML, and is written into the page as it is.</b> The editor only produces the
///     tags of its own schema, but a value reaches it from a database or a request too: sanitize HTML that did
///     not come from this editor before it is bound, and before it is rendered anywhere else with
///     <c>Raw</c>.
///     </para>
/// </remarks>
public sealed partial class UiEditor : Component, IUiFieldControl
{
    private static readonly UiPartMarker Marker = new UiPartMarker("ui-editor").And("ui-control");

    private readonly ElementRef _root = ElementRef.New();

    private readonly int _instance = UiInstanceCounter.Next();

    // What the browser's editor holds, as far as this side knows.
    private string? _shown;

    // The value the app last handed over. Only a CHANGE of it is sent to the browser: an editor given a
    // starting Value and left alone keeps what is typed into it, however often its parent renders.
    private string? _given;

    private bool _mounted;

    /// <summary>The property the editor's HTML is bound to: read to start with, written on every change.</summary>
    public Expression<Func<string?>>? Bind { get; set; }

    /// <summary>Initial content for the editor. Used when not binding.</summary>
    public string? Value { get; set; }

    /// <summary>Raised with the editor's HTML each time it changes; an empty document is an empty string.</summary>
    public Callback<string> OnChange { get; set; }

    /// <summary>Label text displayed above the editor, in a field.</summary>
    public string? Label { get; set; }

    /// <summary>Help text displayed between the label and the editor.</summary>
    public string? Description { get; set; }

    /// <summary>Help text displayed below the editor instead of above it.</summary>
    public string? DescriptionTrailing { get; set; }

    /// <summary>Badge text displayed at the end of the label.</summary>
    public string? Badge { get; set; }

    /// <summary>Placeholder text displayed when the editor is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     Space-separated list of toolbar items to display; <c>|</c> is a separator and <c>~</c> a spacer.
    ///     By default: heading, bold, italic, strike, bullet, ordered, blockquote, link and align.
    /// </summary>
    public string? Toolbar { get; set; }

    /// <summary>Prevents user interaction with the editor.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Applies error styling to the editor.</summary>
    public bool? Invalid { get; set; }

    /// <summary>Extra classes for the editor's box.</summary>
    public string? Class { get; set; }

    /// <summary>The id of the editor's box; derived from the binding or the label when unset.</summary>
    public string? Id { get; set; }

    // Named by its id, its binding or its label, as every field control is; and by itself when nothing names
    // it, because its toolbar points at the editable area by id and two editors must not share one.
    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? "ui-editor-" + _instance.ToString(CultureInfo.InvariantCulture)
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    private string? Current => Bind is { } bind ? ExpressionAccessor.Parse(bind).Getter() as string : Value;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this, Label, Description, DescriptionTrailing, Badge, Invalid == true);
        var composed = (Children ?? []).Where(child => child is not null).ToList();
        var scope = new UiEditorScope(Disabled == true, Current, Placeholder, field.ControlId + "-input");
        var box = Div.Id(field.ControlId)
            .Class(UiClass.Compose(
                "block w-full rounded-lg border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs "
                + "dark:border-white/10 dark:bg-white/10 aria-disabled:shadow-none aria-invalid:border-red-500 aria-invalid:border-b-red-500",
                Class))
            .Data(Marks())
            .Aria(Names(field))
            .Ref(_root);

        return field.Wrap(box[
            Context.Provide(scope)[
                composed.Count != 0 ? composed : [Ui.EditorToolbar.Items(Toolbar), Ui.EditorContent]
            ]
        ]);
    }

    /// <inheritdoc />
    protected override async Task OnFirstRender()
    {
        _shown = _given = Current ?? string.Empty;
        try
        {
            await Mount(_root, UiEditorEngine.Href(LiveOptions.PathBase), Changed);
            _mounted = true;
        }
        catch (InvalidOperationException)
        {
            // No browser behind this render — a prerender, a static first response. The page keeps the first
            // paint, and the editor mounts when a live session renders it.
        }
        catch (JSException)
        {
            // The engine did not load: the host does not serve the file the build wrote into wwwroot. The
            // script has said so in the browser's console; the page keeps the value as plain markup, and stays live.
        }
    }

    /// <inheritdoc />
    protected override async Task OnRendered()
    {
        if (!_mounted)
        {
            return;
        }

        var current = Current ?? string.Empty;
        if (string.Equals(current, _given, StringComparison.Ordinal))
        {
            return;
        }

        _given = current;
        if (!string.Equals(current, _shown, StringComparison.Ordinal))
        {
            _shown = current;
            await SetValue(_root, current);
        }
    }

    /// <inheritdoc />
    protected override async Task OnUnmount()
    {
        if (_mounted)
        {
            _mounted = false;
            await Unmount(_root);
        }
    }

    // The browser's editor changed: the model first, so OnChange sees it already holding the new HTML.
    private async Task Changed(string html)
    {
        _shown = html;
        if (Bind is { } bind)
        {
            var bound = ExpressionAccessor.Parse(bind);
            bound.Setter(html);
            _given = html;
            await BindingHelpers.NotifyAndValidateField(BindingHelpers.ResolveBindingContext(bound.Target), bound.Field);
            if (Consumer(bind) is { } consumer)
            {
                await consumer.StateHasChangedAsync();
            }
        }

        await OnChange.Invoke(html);
    }

    // The component whose render wrote the binding: its model just changed under it, from a callback that
    // is this editor's and would otherwise repaint only this editor. Found the way an event handler's owner
    // is — the expression closes over that component, or over a closure that holds it.
    private static Component? Consumer(Expression<Func<string?>> bind)
    {
        Expression? node = bind.Body;
        while (node is MemberExpression member)
        {
            node = member.Expression;
        }

        return node is ConstantExpression { Value: { } closure } ? DelegateOwner.Resolve(new Func<string?>(closure.ToString)) : null;
    }

    private Dictionary<string, string?> Marks()
    {
        var marks = new Dictionary<string, string?>(Marker.With(null), StringComparer.Ordinal);
        if (Placeholder is { } placeholder)
        {
            marks["placeholder"] = placeholder;
        }

        return marks;
    }

    // Flux names the box "Rich text editor" and, in a field, by its label as well.
    private Dictionary<string, string?> Names(UiWithField field)
    {
        var names = new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal) { ["label"] = "Rich text editor" };
        if (Label is not null)
        {
            names["labelledby"] = field.LabelId;
        }

        if (Disabled == true)
        {
            names["disabled"] = "true";
        }

        return names;
    }
}
