using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// A run of <see cref="UiMenuRadio" /> rows of which one is chosen. Flux UI's <c>flux:menu.radio.group</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control over the chosen value, where Flux takes <c>wire:model</c>: <c>.Bind(() =&gt; view.Sort)</c>, or
/// <c>Value</c> with <c>OnChange</c>. Each <see cref="UiMenuRadio" /> inside says which value it stands for, and
/// the one equal to the group's is the checked one.
/// </para>
/// <code>
/// Ui.MenuRadioGroup.Bind(() =&gt; view.Sort)[
///     Ui.MenuRadio.Value(Sort.Latest)["Latest activity"],
///     Ui.MenuRadio.Value(Sort.Created)["Date created"]
/// ]
/// </code>
/// </remarks>
public sealed partial class UiMenuRadioGroup<T> : Component, IFormControl<T>
{
    /// <summary>Keeps the menu open after a row in the group is picked.</summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T> AfterBind { get; set; }

    // The scope is rebuilt around the value this render resolved.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve<T>(this);
        var scope = new UiMenuRadioScope(
            value => value is T candidate && EqualityComparer<T?>.Default.Equals(candidate, current),
            value => value is T chosen ? UiFormCommit.CommitAsync(this, accessor, context, chosen) : Task.CompletedTask);

        return Div.Role("group").Class(UiClass.Compose("inline", Class)).Data(KeepOpen == true ? KeepsOpen : Group)[
            Context.Provide(scope)[Children ?? []]
        ];
    }

    private static readonly Dictionary<string, string?> Group =
        new(StringComparer.Ordinal) { ["ui-menu-radio-group"] = "" };

    private static readonly Dictionary<string, string?> KeepsOpen =
        new(StringComparer.Ordinal) { ["ui-menu-radio-group"] = "", ["rask-keep-open"] = "" };
}
