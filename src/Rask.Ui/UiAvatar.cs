namespace Rask.Ui;

/// <summary>
/// A person or a thing, as a picture.
/// </summary>
/// <remarks>
/// <see cref="Alt" /> is required. An avatar with no alternative text is announced as its file name or as
/// nothing at all, and it is usually the only thing identifying a row.
/// </remarks>
public sealed partial class UiAvatar : Component
{
    public required string Src { get; set; }

    public required string Alt { get; set; }

    /// <summary>Tailwind sizing for the frame, for example <c>w-12</c>. Defaults to <c>w-10</c>.</summary>
    public string? Size { get; set; }

    /// <summary>Rounds it fully. daisyUI's own examples use <c>rounded-full</c> on the frame.</summary>
    public bool? Round { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("avatar", Class))[
            Div.Class(UiClass.Compose(Size ?? "w-10", Round == false ? "rounded" : "rounded-full"))[
                Img.Src(Src).Alt(Alt)
            ]
        ];
}
