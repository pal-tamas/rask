using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Form : Component<Form.Props>
{
    private readonly Props? _props;

    public Form(Props? props, IEnumerable<Child>? children = null) : base(props, children) => _props = props;
    public Form(Props? props, params Child[] children) : base(props, children) => _props = props;

    protected override string TagName => "form";

    protected override IDisposable? EnterChildrenScope()
    {
        if (_props is null)
        {
            return null;
        }

        if (_props.Model is null && _props.Context is null)
        {
            return null;
        }

        var ctx = ResolveContext(_props);
        return EditContextScope.Push(ctx);
    }

    internal static EditContext ResolveContext(Props p)
    {
        if (p.Context is not null)
        {
            return p.Context;
        }

        if (p.Model is null)
        {
            throw new InvalidOperationException("Form requires Model or Context.");
        }

        var ctx = LiveRenderContext.Current is { } live
            ? live.GetOrCreateEditContext(p.Model)
            : new EditContext(p.Model);
        ctx.AddValidator(new DataAnnotationsValidator());
        return ctx;
    }

    private static Func<FormData, Task> BuildSubmitBridge(Props props, EditContext ctx) =>
        async formData =>
        {
            ctx.Validate();
            ctx.TouchAllRegisteredFields();
            var isValid = !ctx.HasValidationMessages();
            var handler = isValid ? props.OnValidSubmit : props.OnInvalidSubmit;
            if (handler is null)
            {
                if (props.OnSubmit is { } sync)
                {
                    sync(formData);
                }
                else if (props.OnSubmitAsync is { } asyn)
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

    public new sealed record Props(
        string? Enctype = null,
        string? Target = null,
        string? AcceptCharset = null,
        string? Autocomplete = null,
        bool Novalidate = false,
        string? Name = null,
        Action<FormData>? OnSubmit = null,
        Func<FormData, Task>? OnSubmitAsync = null,
        object? Model = null,
        Delegate? OnValidSubmit = null,
        Delegate? OnInvalidSubmit = null,
        EditContext? Context = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data)
    {
        public override IEnumerable<KeyValuePair<string, string?>> ToAttributes()
        {
            foreach (var kv in base.ToAttributes())
            {
                yield return kv;
            }

            if (Enctype is not null)
            {
                yield return new KeyValuePair<string, string?>("enctype", Enctype);
            }

            if (Target is not null)
            {
                yield return new KeyValuePair<string, string?>("target", Target);
            }

            if (AcceptCharset is not null)
            {
                yield return new KeyValuePair<string, string?>("accept-charset", AcceptCharset);
            }

            if (Autocomplete is not null)
            {
                yield return new KeyValuePair<string, string?>("autocomplete", Autocomplete);
            }

            if (Novalidate)
            {
                yield return new KeyValuePair<string, string?>("novalidate", null);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            Delegate? submit;
            if (Model is not null || Context is not null)
            {
                var ctx = ResolveContext(this);
                submit = BuildSubmitBridge(this, ctx);
            }
            else
            {
                submit = (Delegate?)OnSubmit ?? OnSubmitAsync;
            }

            if (submit is not null && LiveRenderContext.Current is { } liveCtx)
            {
                yield return new KeyValuePair<string, string?>("data-rask-on-submit", liveCtx.RegisterHandler(submit));
            }
        }
    }
}
