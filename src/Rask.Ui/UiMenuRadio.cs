namespace Rask;

/// <summary>
/// One choice among several. Flux UI's <c>flux:menu.radio</c>.
/// </summary>
/// <remarks>
/// <para>
/// Inside a <see cref="UiMenuRadioGroup{T}" /> it says which <see cref="Value" /> it stands for, and the group
/// decides whether it is the checked one. On its own — Flux allows a radio straight in a submenu —
/// <see cref="Checked" /> draws it and <see cref="OnClick" /> hears the pick.
/// </para>
/// <para>
/// A <c>menuitemradio</c> with a truthful <c>aria-checked</c>, and <c>data-checked</c> for styling.
/// </para>
/// </remarks>
public sealed partial class UiMenuRadio : Component
{
    /// <summary>The value this row stands for in its group. Compared with the group's by equality.</summary>
    public object? Value { get; set; }

    /// <summary>Whether it is the chosen one, for a radio that is not in a group. A group decides for its own.</summary>
    public bool? Checked { get; set; }

    /// <summary>Shown but not pickable. The keyboard cursor steps over it.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Keeps the menu open after this row is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>Runs when it is picked, after its group has taken the value.</summary>
    public Callback OnClick { get; set; }

    public string? Class { get; set; }

    // Registration happens in Render; see Ui.MenuItem.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var group = Context.Get<UiMenuRadioScope>();
        var level = Context.Get<UiMenuLevel>();
        var chosen = group?.IsChosen(Value) ?? Checked == true;
        var disabled = Disabled == true;
        var ordinal = level?.Scope.Register(level.Parent, UiMenuRow.Label(Children), disabled, isSub: false) ?? -1;

        var button = Button
            .Type(ButtonType.Button)
            .Class(UiClass.Compose(UiMenuRow.Classes(variant: null), Class))
            .Disabled(disabled);
        if (!disabled)
        {
            button = button.OnClick(async () =>
            {
                if (group is not null)
                {
                    await group.Choose(Value).ConfigureAwait(false);
                }

                await OnClick.Invoke().ConfigureAwait(false);
            });
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["checked"] = chosen ? "true" : "false" };
        var data = UiMenuRow.Checkable(chosen, KeepOpen == true);

        Component?[] content = [UiMenuRow.Check(chosen), .. Children ?? []];
        return UiMenuRow.Decorate(button, level, ordinal, "menuitemradio", "ui-menu-radio", aria, data)[content];
    }
}
