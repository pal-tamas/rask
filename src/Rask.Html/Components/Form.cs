using System.Diagnostics.CodeAnalysis;
using System.Text;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Html.Components;

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
//
// TModel is DAM-annotated because the built-in DataAnnotations pass reflects over the model's public
// properties, and so does the graph walk that reaches its sub-objects. Without the annotation the
// trimmer removes those properties from a published WebAssembly build and the form validates NOTHING —
// silently, with a green build and no IL warning. Same annotation, same reason, as BlazorComponent<T>'s
// type parameter; the difference is that this one has to travel through the generated chain, which is
// why ComponentFactoryGenerator repeats it on every declaration it emits.
/// <summary>
///     A form over a model of type <c>TModel</c>. Submission is handled in-process — the bound fields
///     are parsed and validated, then <c>OnValidSubmit</c> or <c>OnInvalidSubmit</c> runs — so there is
///     no <c>action</c> or <c>method</c> to set: the page reacts rather than navigating away.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/form">MDN</see>
/// </summary>
public sealed partial class Form<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TModel>
    : Element
{
    private EditContext? _context;

    private TModel _model = default!;
    protected override string TagName => "form";

    /// <summary>How the form data is encoded. Only <c>multipart/form-data</c> can carry a file upload.</summary>
    public string? Enctype { get; set; }

    /// <summary>Which browsing context the response opens in.</summary>
    public string? Target { get; set; }

    /// <summary>The character encodings the server accepts. Use <c>UTF-8</c>.</summary>
    public string? AcceptCharset { get; set; }

    /// <summary>The default autocomplete behaviour for the controls inside. <c>off</c> is widely ignored by browsers.</summary>
    public string? Autocomplete { get; set; }

    /// <summary>Skips the browser's own validation on submit, leaving validation entirely to the bound validators.</summary>
    public bool? Novalidate { get; set; }

    /// <summary>The form's name, which must be unique in the document.</summary>
    public string? Name { get; set; }
    /// <summary>
    ///     Called on every submit with the raw posted fields, whether validation passed or not. The
    ///     low-level hook: prefer <see cref="OnValidSubmit" />, which hands you the typed model and only
    ///     runs once the form is actually valid.
    /// </summary>
    // Calling one back is `OnSubmit?.Invoke(data)`.
    public Action<FormData>? OnSubmit { get; set; }

    /// <inheritdoc cref="OnSubmit" />
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

    /// <summary>
    ///     Whether this form validates its model with no validator declared — its
    ///     <c>System.ComponentModel.DataAnnotations</c> attributes, plus any validator discovered for
    ///     <c>TModel</c>. <see langword="true" /> by default.
    ///     <para>
    ///         Set it to <see langword="false" /> to take this one form out while leaving the rest of the
    ///         app alone; <see cref="RaskValidation.AutoValidate" /> is the same switch for every form at
    ///         once, and the global off wins — a form cannot opt back in.
    ///     </para>
    /// </summary>
    public bool AutoValidate { get; set; } = true;

    /// <summary>
    ///     The relationship between this form and the destination it submits to — MDN lists
    ///     <c>noopener</c>, <c>noreferrer</c>, <c>external</c> and friends. Only meaningful together with
    ///     <see cref="Target" />, since a form that stays in the page navigates nowhere to relate to.
    ///     <para>
    ///         Declared last on purpose: factory parameters are ordered by declaration span, so putting it
    ///         next to the other attributes would shift the positional index of every callback below it —
    ///         a silent source break for anyone passing them positionally. Same reason as
    ///         <c>Element.Title</c>.
    ///     </para>
    /// </summary>
    public string? Rel { get; set; }

    /// <summary>
    ///     The validation context this form drives. Leave it unset and the form creates and owns one, which
    ///     is what almost every form wants; supply one to share validation state with something outside the
    ///     form, or to inspect it from the page.
    /// </summary>
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

        RegisterBuiltInValidators(ctx);

        // Whichever shape was given; null clears a prior registration so a re-render that drops the
        // validator does not leave a stale callback behind.
        ctx.RegisterFormValidator((Delegate?)Validate ?? ValidateAsync);
        return ctx;
    }

    // The built-in pass. This is what replaced writing `DataAnnotationsValidator,` inside every form:
    // the attributes are already on the model, so there was never anything for that line to say that
    // the model had not said already.
    //
    // Registering on every render is deliberate and free — EditContext.AddValidator dedups by runtime
    // type, so the second and later calls are discarded. That is the same property the old component
    // relied on to survive re-rendering.
    //
    // Order matters and is the decided one: DataAnnotations is an IFieldValidator (the sync stage) and a
    // discovered validator is an IAsyncFieldValidator (the async stage), so EditContext's existing
    // pipeline order already runs attributes first, and its per-field first-error-wins gating means an
    // attribute message shadows a discovered one on the same field. Nothing here reorders anything.
    private void RegisterBuiltInValidators(EditContext ctx)
    {
        if (!RaskValidation.AutoValidate || !AutoValidate)
        {
            return;
        }

        // Ask before building. AddValidator would dedup either way, but only after this method had
        // allocated a validator — and, for FluentValidation, gone to the registry and constructed one
        // with its dependencies — on every render of every form. Both passes are registered together
        // below, so the first one's presence answers for both.
        if (ctx.HasValidator(typeof(DataAnnotationsFieldValidator)))
        {
            return;
        }

        var services = LiveRenderContext.CurrentSync?.Services;

        ctx.AddValidator(new DataAnnotationsFieldValidator(services));

        if (RaskValidation.Resolve(typeof(TModel), services) is { } discovered)
        {
            ctx.AddValidator(discovered);
        }
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

        if (Rel is not null)
        {
            AppendAttr(sb, "rel", Rel);
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
