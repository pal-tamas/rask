using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Select : Component<Select.Props>
{
    public Select(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Select(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "select";

    public new sealed record Props(
        string? Name = null,
        bool Multiple = false,
        bool Required = false,
        bool Disabled = false,
        int? Size = null,
        string? Form = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        Action<string>? OnChange = null,
        Func<string, Task>? OnChangeAsync = null,
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

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            if (Multiple)
            {
                yield return new KeyValuePair<string, string?>("multiple", null);
            }

            if (Required)
            {
                yield return new KeyValuePair<string, string?>("required", null);
            }

            if (Disabled)
            {
                yield return new KeyValuePair<string, string?>("disabled", null);
            }

            if (Size is not null)
            {
                yield return new KeyValuePair<string, string?>("size",
                    Size.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }

            if (Autofocus)
            {
                yield return new KeyValuePair<string, string?>("autofocus", null);
            }

            if (Autocomplete is not null)
            {
                yield return new KeyValuePair<string, string?>("autocomplete", Autocomplete);
            }

            var change = (Delegate?)OnChange ?? OnChangeAsync;
            if (change is not null && LiveRenderContext.Current is { } ctx)
            {
                yield return new KeyValuePair<string, string?>("data-rask-on-change", ctx.RegisterHandler(change));
            }
        }
    }
}
