using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

public sealed class Input : Component<Input.Props>
{
    public Input(Props? props = null) : base(props, null) { }

    protected override string TagName => "input";
    protected override bool SelfClosing => true;

    public new sealed record Props(
        string? Type = null,
        string? Name = null,
        string? Value = null,
        string? Placeholder = null,
        bool Required = false,
        bool Disabled = false,
        bool ReadOnly = false,
        bool Checked = false,
        string? Min = null,
        string? Max = null,
        string? Step = null,
        string? Pattern = null,
        int? Size = null,
        int? MaxLength = null,
        int? MinLength = null,
        bool Multiple = false,
        string? Accept = null,
        string? Alt = null,
        string? Autocomplete = null,
        bool Autofocus = false,
        string? Form = null,
        string? FormAction = null,
        string? FormEnctype = null,
        string? FormMethod = null,
        bool FormNovalidate = false,
        string? FormTarget = null,
        string? List = null,
        string? Src = null,
        int? Width = null,
        int? Height = null,
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

            if (Type is not null)
            {
                yield return new KeyValuePair<string, string?>("type", Type);
            }

            if (Name is not null)
            {
                yield return new KeyValuePair<string, string?>("name", Name);
            }

            if (Value is not null)
            {
                yield return new KeyValuePair<string, string?>("value", Value);
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

            if (Checked)
            {
                yield return new KeyValuePair<string, string?>("checked", null);
            }

            if (Min is not null)
            {
                yield return new KeyValuePair<string, string?>("min", Min);
            }

            if (Max is not null)
            {
                yield return new KeyValuePair<string, string?>("max", Max);
            }

            if (Step is not null)
            {
                yield return new KeyValuePair<string, string?>("step", Step);
            }

            if (Pattern is not null)
            {
                yield return new KeyValuePair<string, string?>("pattern", Pattern);
            }

            if (Size is not null)
            {
                yield return new KeyValuePair<string, string?>("size",
                    Size.Value.ToString(CultureInfo.InvariantCulture));
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

            if (Multiple)
            {
                yield return new KeyValuePair<string, string?>("multiple", null);
            }

            if (Accept is not null)
            {
                yield return new KeyValuePair<string, string?>("accept", Accept);
            }

            if (Alt is not null)
            {
                yield return new KeyValuePair<string, string?>("alt", Alt);
            }

            if (Autocomplete is not null)
            {
                yield return new KeyValuePair<string, string?>("autocomplete", Autocomplete);
            }

            if (Autofocus)
            {
                yield return new KeyValuePair<string, string?>("autofocus", null);
            }

            if (Form is not null)
            {
                yield return new KeyValuePair<string, string?>("form", Form);
            }

            if (FormAction is not null)
            {
                yield return new KeyValuePair<string, string?>("formaction", FormAction);
            }

            if (FormEnctype is not null)
            {
                yield return new KeyValuePair<string, string?>("formenctype", FormEnctype);
            }

            if (FormMethod is not null)
            {
                yield return new KeyValuePair<string, string?>("formmethod", FormMethod);
            }

            if (FormNovalidate)
            {
                yield return new KeyValuePair<string, string?>("formnovalidate", null);
            }

            if (FormTarget is not null)
            {
                yield return new KeyValuePair<string, string?>("formtarget", FormTarget);
            }

            if (List is not null)
            {
                yield return new KeyValuePair<string, string?>("list", List);
            }

            if (Src is not null)
            {
                yield return new KeyValuePair<string, string?>("src", Src);
            }

            if (Width is not null)
            {
                yield return new KeyValuePair<string, string?>("width",
                    Width.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (Height is not null)
            {
                yield return new KeyValuePair<string, string?>("height",
                    Height.Value.ToString(CultureInfo.InvariantCulture));
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
