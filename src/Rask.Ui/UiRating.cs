using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A rating, as stars.
/// </summary>
/// <remarks>
/// <para>
/// Radio inputs sharing a name, which is what makes it settable with no script and reachable by keyboard.
/// </para>
/// <para>
/// The first radio is hidden and represents "no rating": without it a rating can be raised and lowered but
/// never cleared, because a radio group offers no way back to none. Picking it commits <c>0</c>.
/// </para>
/// <para>
/// A form control over an <c>int</c>, concretely rather than generically — a star count is a whole
/// number between zero and <see cref="Max" />. <c>.Bind(() =&gt; model.Score)</c> two-way binds;
/// <see cref="Value" /> with <see cref="OnChange" /> leaves it with the parent.
/// </para>
/// </remarks>
public sealed partial class UiRating : Component, IFormControl<int>
{
    /// <summary>The name the radios share. Must be unique on the page.</summary>
    public required string Group { get; set; }

    /// <summary>The accessible name for the group.</summary>
    public new required string Label { get; set; }

    public int? Max { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     How many stars are lit; <c>0</c> is no rating. Not nullable — see
    ///     <see cref="UiCheckbox.Value" /> — which suits a rating: unrated and zero stars are the same
    ///     state here, and the hidden first radio is how a reader gets back to it.
    /// </remarks>
    public int Value { get; set; }

    /// <inheritdoc />
    public Action<int>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<int, Task>? OnChangeAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<int>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<int>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<int>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<int>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<int, Task>? AfterBindAsync { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (acc, ctx, current) = UiFormCommit.Resolve<int>(this);
        var max = Math.Max(Max ?? 5, 0);

        return Div
            .Role("radiogroup")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "rating",
                Size is { } size ? UiClassNames.RatingSize(size) : "",
                Class))[
            Input
                .Value("0")
                .Checked(current == 0)
                .OnChangeAsync(_ => UiFormCommit.CommitAsync(this, acc, ctx, 0))
                .Type(InputType.Radio)
                .Name(Group)
                .Class("rating-hidden")
                .Aria(new Dictionary<string, string?> { ["label"] = "No rating" }),
            Enumerable.Range(1, max).Select(star =>
                Input
                    .Value(star.ToString(CultureInfo.InvariantCulture))
                    .Checked(star == current)
                    .OnChangeAsync(_ => UiFormCommit.CommitAsync(this, acc, ctx, star))
                    .Key(star)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("mask mask-star-2")
                    .Aria(new Dictionary<string, string?>
                    {
                        ["label"] = $"{star.ToString(CultureInfo.InvariantCulture)} of "
                                    + max.ToString(CultureInfo.InvariantCulture),
                    }))
        ];
    }
}
