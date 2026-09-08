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
/// never cleared, because a radio group offers no way back to none.
/// </para>
/// </remarks>
public sealed partial class UiRating : Component
{
    /// <summary>The name the radios share. Must be unique on the page.</summary>
    public required string Group { get; set; }

    /// <summary>The accessible name for the group.</summary>
    public required string Label { get; set; }

    public int? Value { get; set; }

    public int? Max { get; set; }

    public UiSize? Size { get; set; }

    public Action<int>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Role("radiogroup")
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "rating",
                Size is { } size ? UiClassNames.RatingSize(size) : "",
                Class))[
            Input
                .Value("0")
                .Checked(Value is null or 0)
                .Type(InputType.Radio)
                .Name(Group)
                .Class("rating-hidden")
                .Aria(new Dictionary<string, string?> { ["label"] = "No rating" }),
            Enumerable.Range(1, Math.Max(Max ?? 5, 0)).Select(star =>
            {
                var input = Input
                    .Value(star.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Checked(star == Value)
                    .Key(star)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("mask mask-star-2")
                    .Aria(new Dictionary<string, string?>
                    {
                        ["label"] = $"{star.ToString(System.Globalization.CultureInfo.InvariantCulture)} of "
                                    + (Max ?? 5).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });

                return OnChange is { } change ? input.OnChange(_ => change(star)) : input;
            })
        ];
}
