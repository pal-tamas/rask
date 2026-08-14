using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// A <form> bound to a model. Generic over TModel, which is what types everything downstream: the
// submit handlers receive the model itself rather than a Delegate the component has to DynamicInvoke,
// and the cross-field validator is a `Validate<TModel>` rather than something checked at runtime.
//
// It used to be non-generic, with the typing supplied by a [FactoryGeneric] overload that narrowed
// `object?`/`Delegate?` down per call. That worked only while the factory existed — a chain has no
// overloads to narrow through — so the generics moved onto the component, and the attribute and its
// whole generator path went with them. The three-way validator fan-out went too: it existed so a sync
// and an async validator could each be a required, correctly-typed PARAMETER, and as two setters they
// simply coexist.
public sealed partial class Form<TModel> : Element
{
    private EditContext? _context;

    private TModel _model = default!;
    protected override string TagName => "form";

    public string? Enctype { get; set; }
    public string? Target { get; set; }
    public string? AcceptCharset { get; set; }
    public string? Autocomplete { get; set; }
    public bool? Novalidate { get; set; }
    public string? Name { get; set; }
    // Calling one back is `OnSubmit?.Invoke(data)`.
    public Action<FormData>? OnSubmit { get; set; }

    public Func<FormData, Task>? OnSubmitAsync { get; set; }

    // Pre-registers the form's EditContext with LiveRenderContext (creating it if needed) and
    // walks the model graph so descendant sub-objects also resolve to the same context. Without
    // this, a nested binding like Input.Bind(() => model.Address.Street) — whose acc.Target is
    // model.Address — would GetOrCreateEditContext(model.Address) at factory time and end up
    // writing field events into a separate empty EditContext, never reaching the validators
    // that self-registered into the form's context. Setter runs every render (generated factory
    // re-applies properties on cached instances), keeping the registration fresh when sub-
    // object references are swapped between renders.

    /// <summary>The model this form binds to. Every field inside it resolves against this object.</summary>
    public required TModel Model
    {
        get => _model;
        set
        {
            _model = value;
            if (value is not null && LiveRenderContext.CurrentSync is { } live)
            {
                var ctx = live.GetOrCreateEditContext(value);
                RegisterSubGraph(live, ctx, value);
            }
        }
    }

    /// <summary>Runs on submit when every field passes validation.</summary>
    [AutoCallback]
    public Action<TModel>? OnValidSubmit { get; set; }

    /// <inheritdoc cref="OnValidSubmit" />
    [AutoCallback]
    public Func<TModel, Task>? OnValidSubmitAsync { get; set; }

    /// <summary>Runs on submit when validation fails, so the page can react rather than sit silent.</summary>
    [AutoCallback]
    public Action<TModel>? OnInvalidSubmit { get; set; }

    /// <inheritdoc cref="OnInvalidSubmit" />
    [AutoCallback]
    public Func<TModel, Task>? OnInvalidSubmitAsync { get; set; }

    /// <summary>
    ///     Cross-field validation for the form as a whole. Messages attach to the model rather than to a
    ///     field, so they surface in ValidationSummary and any field-less ValidationMessage.
    /// </summary>
    public Validate<TModel>? Validate { get; set; }

    /// <inheritdoc cref="Validate" />
    public ValidateAsync<TModel>? ValidateAsync { get; set; }

    public EditContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            // Pre-register the user-supplied context so sibling Input/Select/Textarea bound
            // factories — which run inside the same parent Render() pass, before this Form's
            // EnterChildrenScope pushes the scope — resolve to this exact instance through
            // LiveRenderContext.GetOrCreateEditContext(model). Walk the model graph so nested
            // bindings (acc.Target = a sub-object) also land on this context rather than
            // auto-creating a separate one keyed by the sub-object reference.
            if (value is not null && LiveRenderContext.CurrentSync is { } live)
            {
                live.RegisterEditContext(value);
                RegisterSubGraph(live, value, value.Model);
            }
        }
    }

    private static void RegisterSubGraph(LiveRenderContext live, EditContext ctx, object root)
    {
        foreach (var node in ModelGraphWalker.Walk(root))
        {
            if (!ReferenceEquals(node, root))
            {
                live.RegisterEditContextForKey(node, ctx);
            }
        }
    }

    protected override IDisposable? EnterChildrenScope()
    {
        if (Model is null && Context is null)
        {
            return null;
        }

        var ctx = ResolveContext();
        return EditContextScope.Push(ctx);
    }

    internal EditContext ResolveContext()
    {
        EditContext ctx;
        if (Context is not null)
        {
            ctx = Context;
        }
        else
        {
            if (Model is null)
            {
                // Defensive, and worth saying so: both of today's callers already gate on
                // `Model is not null || Context is not null`, so this cannot fire from a render — a Form
                // with neither is a plain <form>, deliberately. It is kept for the next caller, and
                // reworded because the old text ("Form requires Model or Context.") named neither the
                // form nor the API shape, and "Context" is ambiguous between the Context<T> component
                // and the EditContext this actually wants.
                throw new InvalidOperationException(
                    $"Form{Describe()} has neither a model nor an EditContext, so there is nothing for "
                    + "its fields to bind to. Open the chain with the model — Form.Model(model)[ … ] — "
                    + "or hand it an existing EditContext with Form.Model(model).Context(editContext)[ … ].");
            }

            ctx = LiveRenderContext.CurrentSync is { } live
                ? live.GetOrCreateEditContext(Model)
                : new EditContext(Model);
        }

        // RegisterFormValidator(null) clears any prior registration so a re-render that
        // drops the Validate parameter doesn't leave a stale callback behind.
        // Whichever shape was given; null clears a prior registration so a re-render that drops the
        // validator does not leave a stale callback behind.
        ctx.RegisterFormValidator((Delegate?)Validate ?? ValidateAsync);
        return ctx;
    }

    // Which form, when a page has several. Nothing identifies a Form intrinsically, so this leans on
    // whatever the author already wrote — an id, a name, a class — and says nothing when there is none,
    // rather than inventing a label that would not help anyone find it.
    private string Describe() =>
        Id is { Length: > 0 } id ? $" '#{id}'"
        : Name is { Length: > 0 } name ? $" '{name}'"
        : Class is { Length: > 0 } cls ? $" '.{cls}'"
        : string.Empty;

    private Func<FormData, Task> BuildSubmitBridge(EditContext ctx) =>
        async formData =>
        {
            await ctx.ValidateAsync().ConfigureAwait(false);
            ctx.TouchAllRegisteredFields();
            var isValid = !ctx.HasValidationMessages();
            var onModel = isValid ? OnValidSubmit : OnInvalidSubmit;
            var onModelAsync = isValid ? OnValidSubmitAsync : OnInvalidSubmitAsync;
            if (onModel is null && onModelAsync is null)
            {
                // No model-shaped handler: fall back to the raw FormData pair, which is what a form that
                // only wants the posted values uses.
                if (OnSubmit is { } sync)
                {
                    sync(formData);
                }
                else if (OnSubmitAsync is { } asyn)
                {
                    await asyn(formData).ConfigureAwait(false);
                }

                return;
            }

            // Typed, so the model goes straight to the handler — the non-generic Form had to
            // DynamicInvoke here, because all it held was a Delegate.
            var model = (TModel)ctx.Model;
            if (onModel is { } handler)
            {
                handler(model);
            }
            else if (onModelAsync is { } handlerAsync)
            {
                await handlerAsync(model).ConfigureAwait(false);
            }
        };

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Enctype is not null)
        {
            AppendAttr(sb, "enctype", Enctype);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }

        if (AcceptCharset is not null)
        {
            AppendAttr(sb, "accept-charset", AcceptCharset);
        }

        if (Autocomplete is not null)
        {
            AppendAttr(sb, "autocomplete", Autocomplete);
        }

        if (Novalidate is true)
        {
            AppendAttr(sb, "novalidate", null);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        Delegate? submit;
        if (Model is not null || Context is not null)
        {
            var ctx = ResolveContext();
            submit = BuildSubmitBridge(ctx);
        }
        else
        {
            submit = (Delegate?)OnSubmit ?? OnSubmitAsync;
        }

        if (submit is not null && LiveRenderContext.CurrentSync is { } liveCtx)
        {
            AppendAttr(sb, "data-rask-on-submit", liveCtx.RegisterHandler(submit));
        }
    }
}
