using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

// [FactoryGeneric] also emits a `Form<TModel>(TModel Model, Action<TModel>? OnValidSubmit, ...)`
// overload that narrows Model + the submit-handler delegates from the non-generic factory's
// `object?` / `Delegate?` shapes to typed counterparts. The generic overload synthesises a
// `Func<TModel, Task>? XAsync` sibling for each named delegate property and collapses the
// two back into a single `Delegate?` argument before forwarding to the non-generic factory.
[FactoryGeneric("TModel",
    ModelProperty = nameof(Model),
    TypedDelegateProperties = new[] { nameof(OnValidSubmit), nameof(OnInvalidSubmit) })]
public sealed class Form : Component
{
    protected override string TagName => "form";

    public string? Enctype { get; set; }
    public string? Target { get; set; }
    public string? AcceptCharset { get; set; }
    public string? Autocomplete { get; set; }
    public bool Novalidate { get; set; }
    public string? Name { get; set; }
    public Action<FormData>? OnSubmit { get; set; }
    public Func<FormData, Task>? OnSubmitAsync { get; set; }
    public object? Model { get; set; }
    public Delegate? OnValidSubmit { get; set; }
    public Delegate? OnInvalidSubmit { get; set; }

    private EditContext? _context;
    public EditContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            // Pre-register the user-supplied context so sibling Input/Select/Textarea bound
            // factories — which run inside the same parent Render() pass, before this Form's
            // EnterChildrenScope pushes the scope — resolve to this exact instance through
            // LiveRenderContext.GetOrCreateEditContext(model).
            if (value is not null)
            {
                LiveRenderContext.Current?.RegisterEditContext(value);
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
        if (Context is not null)
        {
            return Context;
        }

        if (Model is null)
        {
            throw new InvalidOperationException("Form requires Model or Context.");
        }

        var ctx = LiveRenderContext.Current is { } live
            ? live.GetOrCreateEditContext(Model)
            : new EditContext(Model);
        ctx.AddValidator(new DataAnnotationsValidator());
        return ctx;
    }

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

    protected override IEnumerable<KeyValuePair<string, string?>> BuildAttributes()
    {
        foreach (var kv in base.BuildAttributes()) yield return kv;
        if (Enctype is not null) yield return new("enctype", Enctype);
        if (Target is not null) yield return new("target", Target);
        if (AcceptCharset is not null) yield return new("accept-charset", AcceptCharset);
        if (Autocomplete is not null) yield return new("autocomplete", Autocomplete);
        if (Novalidate) yield return new("novalidate", null);
        if (Name is not null) yield return new("name", Name);

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

        if (submit is not null && LiveRenderContext.Current is { } liveCtx)
        {
            yield return new("data-rask-on-submit", liveCtx.RegisterHandler(submit));
        }
    }
}
