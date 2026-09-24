using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A menu item that is on or off — "Show archived", "Word wrap".
/// </summary>
/// <remarks>
/// <para>
/// A form control like every other in the kit: <c>.Bind(() =&gt; view.ShowArchived)</c> writes back to the model,
/// or <c>Value</c> with <c>OnChange</c> leaves the value to the parent. It is a <c>menuitemcheckbox</c> with a
/// truthful <c>aria-checked</c>, and <c>data-checked</c> for styling.
/// </para>
/// <para>
/// Picking it does NOT close the dropdown by default, unlike an ordinary item: a menu of switches is a menu
/// someone flips several of. <see cref="KeepOpen" /> set to <see langword="false" /> closes it after a pick.
/// </para>
/// </remarks>
public sealed partial class UiMenuCheckbox : Component, IFormControl<bool>
{
    public new required string Text { get; set; }

    public Ui.IconName? Icon { get; set; }

    /// <summary>A keyboard shortcut shown at the end of the row.</summary>
    public string? Kbd { get; set; }

    public bool? Disabled { get; set; }

    /// <summary>Keeps the dropdown open after a pick. On unless this is <see langword="false" />.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public bool Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<bool>? OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<bool>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<bool>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<bool>? AfterBind { get; set; }

    // Registration happens in Render; see Ui.MenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve<bool>(this);
        var level = Context.Get<UiMenuLevel>();
        var ordinal = level?.Scope.Register(level.Parent, Text, Disabled == true, isSub: false) ?? -1;

        var button = Button
            .Type("button")
            .Class(UiClass.Compose(
                level is not null && ordinal == level.Scope.Active ? "menu-focus" : "",
                Disabled == true ? "menu-disabled" : ""));
        if (Disabled != true)
        {
            button = button.OnClick(() => UiFormCommit.CommitAsync<bool>(this, accessor, context, !current));
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["checked"] = current ? "true" : "false" };
        if (Disabled == true)
        {
            aria["disabled"] = "true";
        }

        button = UiMenuItemMarkup.AsMenuItem(button, level, ordinal, "menuitemcheckbox", aria, KeepOpen != false, current);

        return Li.Role(level is null ? null : "none").Class(Class)[
            button[global::Rask.UiMenuItem.Row(Icon, Text, Kbd, trailing: null, global::Rask.UiMenuItem.Indicator(current))]
        ];
    }
}
