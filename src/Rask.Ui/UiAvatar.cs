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

    /// <summary>How big the frame is. Defaults to a 2.5rem frame.</summary>
    /// <remarks>
    ///     The same <see cref="UiSize" /> every other component takes. It used to be a Tailwind class string,
    ///     which is invisible to the kit's own stylesheet build: a width nobody else on the page wrote was
    ///     never compiled, and the avatar silently kept its default size.
    /// </remarks>
    public UiSize? Size { get; set; }

    /// <summary>Rounds it fully. daisyUI's own examples use <c>rounded-full</c> on the frame.</summary>
    public bool? Round { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("avatar", Class))[
            Div.Class(UiClass.Compose(UiClassNames.AvatarSize(Size ?? UiSize.Default), Round == false ? "rounded" : "rounded-full"))[
                Img.Src(Src).Alt(Alt)
            ]
        ];
}
