using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

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
    public EditContext? Context { get; set; }

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
            ctx.Validate();
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
