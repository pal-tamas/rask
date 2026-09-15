using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A run of menu items of which exactly one is chosen — "Sort by name / date / size".
/// </summary>
/// <remarks>
/// <para>
/// A form control over the chosen value: <c>.Bind(() =&gt; view.Sort)</c> or <c>Value</c> with <c>OnChange</c>,
/// and <see cref="Options" /> in the same shape <see cref="UiSelect{T}" /> takes. Each option is a
/// <c>menuitemradio</c> with a truthful <c>aria-checked</c> and <c>data-checked</c>.
/// </para>
/// <para>
/// The options sit at the menu's own level, so the arrow keys move through them like any other rows. Picking one
/// closes the dropdown, as choosing from a list does; <see cref="KeepOpen" /> keeps it up.
/// </para>
/// </remarks>
public sealed partial class UiMenuRadioGroup<T> : Component, IFormControl<T>
{
    /// <summary>The options: the value stored, and the words shown.</summary>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>The words above the options.</summary>
    public string? Heading { get; set; }

    /// <summary>Marks options unpickable. The keyboard cursor skips them.</summary>
    public Fn<T, bool>? OptionDisabled { get; set; }

    /// <summary>Keeps the dropdown open after a pick.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T>? OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T>? AfterBind { get; set; }

    // Registration happens in Render; see UiMenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve<T>(this);
        var level = Context.Get<UiMenuLevel>();

        return
        [
            Heading is null
                ? null
                : Li.Class("menu-title").Role(level is null ? null : "presentation")[Heading],
            .. Options.Select(option => OptionRow(option, level, accessor, context, current))
        ];
    }

    private Component OptionRow(
        (T Value, string Text) option,
        UiMenuLevel? level,
        ExpressionAccessor.Accessor? accessor,
        EditContext? context,
        T? current)
    {
        var disabled = OptionDisabled?.Invoke(option.Value) == true;
        var chosen = EqualityComparer<T?>.Default.Equals(option.Value, current);
        var ordinal = level?.Scope.Register(level.Parent, option.Text, disabled, isSub: false) ?? -1;

        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                level is not null && ordinal == level.Scope.Active ? "menu-focus" : "",
                disabled ? "menu-disabled" : ""));
        if (!disabled)
        {
            button = button.OnClick(() => UiFormCommit.CommitAsync(this, accessor, context, option.Value));
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["checked"] = chosen ? "true" : "false" };
        if (disabled)
        {
            aria["disabled"] = "true";
        }

        button = UiMenuItemMarkup.AsMenuItem(button, level, ordinal, "menuitemradio", aria, KeepOpen == true, chosen);

        return Li.Key(option.Text).Role(level is null ? null : "none")[
            button[global::Rask.Ui.UiMenuItem.Row(icon: null, option.Text, kbd: null, trailing: null, global::Rask.Ui.UiMenuItem.Indicator(chosen))]
        ];
    }
}
