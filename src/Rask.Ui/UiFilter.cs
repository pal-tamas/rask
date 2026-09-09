using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A row of choices where picking one narrows a list, with a way back to all of them.
/// </summary>
/// <remarks>
/// <para>
/// Radios rather than buttons, and that is what makes it work with no script: daisyUI's <c>filter</c>
/// hides the unpicked options once one is chosen and shows the reset in their place, entirely in CSS.
/// The group also gives a keyboard the arrow-key behaviour a row of buttons would have to reimplement.
/// </para>
/// <para>
/// This is the kit's control for a whole radio GROUP, so it is the one that binds the chosen value —
/// <see cref="UiRadio" /> is a single option and binds only its own checked state.
/// </para>
/// </remarks>
public sealed partial class UiFilter<T> : Component, IFormControl<T>
{
    /// <summary>The radio group's name, so two filters on one page do not fight.</summary>
    public required string Group { get; set; }

    /// <summary>
    ///     The options offered, in order: the value chosen, and the words shown. The pair is what pins
    ///     <typeparamref name="T" />, the way <c>Form.Model(m)</c> pins its model type.
    /// </summary>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>The accessible name on the reset control. Defaults to "All".</summary>
    public string? ResetLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     The chosen option. The reset commits <c>default</c> — <see langword="null" /> for a reference
    ///     type, which is what "no filter" means; for a value type it is that type's zero, so a filter
    ///     over one wants either a zero that genuinely means "unfiltered" or a nullable
    ///     <typeparamref name="T" />.
    /// </remarks>
    public T? Value { get; set; }

    /// <inheritdoc />
    public Callback<T>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<T>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<T>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Callback<T>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<T>(this);

        // A div, not a <form>. daisyUI's own example wraps this in one so a reset BUTTON can clear it,
        // but the reset here is a radio carrying `filter-reset` — the group already holds the state, and
        // a nested <form> inside somebody else's form is invalid HTML.
        return Div.Class(UiClass.Compose("filter", Class))[
            Input
                .Value(string.Empty)
                .Checked(IsChosen(default, current))
                .OnChange(_ => UiFormCommit.CommitAsync(this, acc, ctx, default!))
                .Type(InputType.Radio)
                .Name(Group)
                .Class("btn btn-square filter-reset")
                .Aria(new Dictionary<string, string?> { ["label"] = ResetLabel ?? "All" }),
            Options.Select(option =>
            {
                var (value, text) = option;

                // daisyUI draws each option's caption from the input's own value, so opening the chain on
                // the text is not a workaround — on this control the value attribute IS the label.
                return Input
                    .Value(text)
                    .Checked(IsChosen(value, current))
                    .OnChange(_ => UiFormCommit.CommitAsync(this, acc, ctx, value))
                    .Key(text)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("btn")
                    .Aria(new Dictionary<string, string?> { ["label"] = text });
            })
        ];
    }

    private static bool IsChosen(T? candidate, T? current) =>
        EqualityComparer<T?>.Default.Equals(candidate, current);
}
