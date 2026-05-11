using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Textarea : Component<Textarea.Props>
{
    public Textarea(Props? props, IEnumerable<Child>? children = null) : base(props, children) { }
    public Textarea(Props? props, params Child[] children) : base(props, children) { }

    protected override string TagName => "textarea";

    public new sealed record Props(
        string? Name = null,
        int? Rows = null,
        int? Cols = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        int? MaxLength = null,
        int? MinLength = null,
        string? Wrap = null,
        bool Autofocus = false,
        string? Autocomplete = null,
        string? Form = null,
        string? Dirname = null,
        Action<string>? OnInput = null,
        Action<string>? OnChange = null,
        Func<string, Task>? OnInputAsync = null,
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

            if (Rows is not null)
            {
                yield return new KeyValuePair<string, string?>("rows",
                    Rows.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Cols is not null)
            {
                yield return new KeyValuePair<string, string?>("cols",
                    Cols.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Placeholder is not null)
            {
                yield return new KeyValuePair<string, string?>("placeholder", Placeholder);
            }

            if (Required)
            {
                yield return new KeyValuePair<string, string?>("required", null);
            }

            if (Disabled)
            {
                yield return new KeyValuePair<string, string?>("disabled", null);
            }

            if (ReadOnly)
            {
                yield return new KeyValuePair<string, string?>("readonly", null);
            }

            if (MaxLength is not null)
            {
                yield return new KeyValuePair<string, string?>("maxlength",
                    MaxLength.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (MinLength is not null)
            {
                yield return new KeyValuePair<string, string?>("minlength",
                    MinLength.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Wrap is not null)
            {
                yield return new KeyValuePair<string, string?>("wrap", Wrap);
            }

            if (Autofocus)
            {
                yield return new KeyValuePair<string, string?>("autofocus", null);
            }

            if (Autocomplete is not null)
            {
                yield return new KeyValuePair<string, string?>("autocomplete", Autocomplete);
            }

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }

            if (Dirname is not null)
            {
                yield return new KeyValuePair<string, string?>("dirname", Dirname);
            }

            if (LiveRenderContext.Current is { } ctx)
            {
                var input = (Delegate?)OnInput ?? OnInputAsync;
                if (input is not null)
                {
                    yield return new KeyValuePair<string, string?>("data-rask-on-input", ctx.RegisterHandler(input));
                }

                var change = (Delegate?)OnChange ?? OnChangeAsync;
                if (change is not null)
                {
                    yield return new KeyValuePair<string, string?>("data-rask-on-change", ctx.RegisterHandler(change));
                }
            }
        }
    }
}
