using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// [FactoryGeneric] also emits a `Form<TModel>(TModel Model, Action<TModel>? OnValidSubmit, ...)`
// overload that narrows Model + the submit-handler delegates from the non-generic factory's
// `object?` / `Delegate?` shapes to typed counterparts. The generic overload synthesises a
// `Func<TModel, Task>? XAsync` sibling for each TypedDelegateProperties name and collapses
// the two back into a single `Delegate?` argument before forwarding to the non-generic factory.
//
// `Validate` rides on TypedValidatorProperties, which fans out into three overloads of the
// generic factory — no `Validate`, typed sync `Validate<TModel>`, and typed async
// `ValidateAsync<TModel>` (the shared validator delegate types in Rask.Core.Forms) — so callers
// can pass a bare lambda without a delegate cast.
[FactoryGeneric("TModel",
    ModelProperty = nameof(Model),
    TypedDelegateProperties = new[] { nameof(OnValidSubmit), nameof(OnInvalidSubmit) },
    TypedValidatorProperties = new[] { nameof(Validate) })]
public sealed class Form : Element
{
    private EditContext? _context;

    private object? _model;
    protected override string TagName => "form";

    public string? Enctype { get; set; }
    public string? Target { get; set; }
    public string? AcceptCharset { get; set; }
    public string? Autocomplete { get; set; }
    public bool? Novalidate { get; set; }
    public string? Name { get; set; }
    public Callback<FormData>? OnSubmit { get; set; }

    public CallbackAsync<FormData>? OnSubmitAsync { get; set; }

    // Pre-registers the form's EditContext with LiveRenderContext (creating it if needed) and
    // walks the model graph so descendant sub-objects also resolve to the same context. Without
    // this, a nested binding like Input(() => model.Address.Street) — whose acc.Target is
    // model.Address — would GetOrCreateEditContext(model.Address) at factory time and end up
    // writing field events into a separate empty EditContext, never reaching the validators
    // that self-registered into the form's context. Setter runs every render (generated factory
    // re-applies properties on cached instances), keeping the registration fresh when sub-
    // object references are swapped between renders.
    public object? Model
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

    public Delegate? OnValidSubmit { get; set; }
    public Delegate? OnInvalidSubmit { get; set; }

    // Cross-field validation rule. Accepts either:
    //   sync   — Validate<TModel>
    //   async  — ValidateAsync<TModel>
    // Messages produced here attach to FieldIdentifier(Model, "") — i.e. they surface in
    // ValidationSummary and any field-less ValidationMessage, not against a specific input.
    public Delegate? Validate { get; set; }

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
                    + "its fields to bind to. Pass the model as the first argument — Form(model)[ … ] — "
                    + "or hand it an existing EditContext with Form(Context: editContext)[ … ].");
            }

            ctx = LiveRenderContext.CurrentSync is { } live
                ? live.GetOrCreateEditContext(Model)
                : new EditContext(Model);
        }

        // RegisterFormValidator(null) clears any prior registration so a re-render that
        // drops the Validate parameter doesn't leave a stale callback behind.
        ctx.RegisterFormValidator(Validate);
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
            var handler = isValid ? OnValidSubmit : OnInvalidSubmit;
            if (handler is null)
            {
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

            var result = handler.DynamicInvoke(ctx.Model);
            if (result is Task t)
            {
                await t.ConfigureAwait(false);
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
