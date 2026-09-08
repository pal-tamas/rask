namespace Rask.Ui;

/// <summary>
/// Work in progress, with no idea how much is left.
/// </summary>
/// <remarks>
/// The spinner is <c>aria-hidden</c> and the words beside it are what gets announced. A bare spinner tells
/// a screen reader nothing at all, and "Loading" read once is worth more than an animation.
/// </remarks>
public sealed partial class UiLoading : Component
{
    /// <summary>What is being waited for. Announced; the spinner itself is decorative.</summary>
    public new required string Text { get; set; }

    /// <summary>What it looks like while it spins. Cosmetic; every shape says the same thing.</summary>
    public UiLoadingShape? Shape { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Role("status").Class(UiClass.Compose("inline-flex items-center gap-2", Class))[
            Span
                .Class(UiClass.Compose(
                    "loading",
                    UiClassNames.LoadingShape(Shape ?? UiLoadingShape.Spinner),
                    Size is { } size ? UiClassNames.LoadingSize(size) : ""))
                .Attributes(("aria-hidden", "true")),
            Span[Text]
        ];
}
