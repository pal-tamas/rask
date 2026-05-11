using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Button : Component<Button.Props>
{
    public Button(Props? props, IEnumerable<Child>? children = null)
        : base(props, children)
    {
    }

    public Button(Props? props, params Child[] children)
        : base(props, children)
    {
    }

    protected override string TagName => "button";

    public new sealed record Props(
        string? Type = null,
        bool Disabled = false,
        string? Name = null,
        string? Value = null,
        Action? OnClick = null,
        Func<Task>? OnClickAsync = null,
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

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Disabled)
            {
                yield return new KeyValuePair<string, string?>("disabled", null);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            if (Value is not null)
            {
                yield return new KeyValuePair<string, string?>("value", Value);
            }

            var click = (Delegate?)OnClick ?? OnClickAsync;
            if (click is not null && LiveRenderContext.Current is { } ctx)
            {
                yield return new KeyValuePair<string, string?>("data-rask-on-click", ctx.RegisterHandler(click));
            }
        }
    }
}
