using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A row that is on or off — "Draft", "Word wrap". Flux UI's <c>flux:menu.checkbox</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control like every other in the kit, where Flux takes <c>wire:model</c> and <c>checked</c>:
/// <c>.Bind(() =&gt; view.ShowArchived)</c> writes back to the model, or <c>Value</c> with <c>OnChange</c> leaves
/// the value to the parent. It is a <c>menuitemcheckbox</c> with a truthful <c>aria-checked</c>, and
/// <c>data-checked</c> for styling.
/// </para>
/// <para>
/// Picking it closes the menu, as picking any row does. <see cref="KeepOpen" /> — on the row, or on the menu —
/// is for the menu someone ticks several of.
/// </para>
/// </remarks>
public sealed partial class UiMenuCheckbox : Component, IFormControl<bool>
{
    /// <summary>Shown but not pickable. The keyboard cursor steps over it.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Keeps the menu open after this row is picked.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public bool Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<bool> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<bool>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<bool>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<bool> AfterBind { get; set; }

    // Registration happens in Render; see Ui.MenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve<bool>(this);
        var level = Context.Get<UiMenuLevel>();
        var disabled = Disabled == true;
        var ordinal = level?.Scope.Register(level.Parent, UiMenuRow.Label(Children), disabled, isSub: false) ?? -1;

        var button = Button
            .Type(ButtonType.Button)
            .Class(UiClass.Compose(UiMenuRow.Classes(variant: null), Class))
            .Disabled(disabled);
        if (!disabled)
        {
            button = button.OnClick(() => UiFormCommit.CommitAsync<bool>(this, accessor, context, !current));
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["checked"] = current ? "true" : "false" };
        var data = UiMenuRow.Checkable(current, KeepOpen == true);

        Component?[] content = [UiMenuRow.Check(current), .. Children ?? []];
        return UiMenuRow.Decorate(button, level, ordinal, "menuitemcheckbox", "ui-menu-checkbox", aria, data)[content];
    }
}
