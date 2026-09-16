namespace Rask.Ui;

/// <summary>
/// A person or a thing, as a picture — or as the initials of its name when there is no picture.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Src" /> used to be required, which made this unusable for the case it is most often reached for:
/// a signed-in person. Most accounts have no picture, and a broken image is worse than a monogram. Give it a
/// <see cref="Name" /> and it draws the initials instead — the same fallback <see cref="UiProfile" /> uses,
/// because they are the same question.
/// </para>
/// <para>
/// It still has to be NAMED. With a picture that is <see cref="Alt" />; with initials it is the
/// <see cref="Name" />, since "TP" read aloud tells a reader nothing. An avatar announced as its file name, or
/// as nothing at all, is usually the only thing identifying a row.
/// </para>
/// </remarks>
public sealed partial class UiAvatar : Component
{
    /// <summary>The picture. Without one, the initials of <see cref="Name" /> are drawn instead.</summary>
    public string? Src { get; set; }

    /// <summary>
    ///     What the picture shows, for a reader who cannot see it. Required when there IS a picture.
    /// </summary>
    public string? Alt { get; set; }

    /// <summary>Who or what this is — the source of the initials, and the accessible name without a picture.</summary>
    public string? Name { get; set; }

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
    protected override Component? Render()
    {
        var frame = UiClass.Compose(
            UiClassNames.AvatarSize(Size ?? UiSize.Default),
            Round == false ? "rounded" : "rounded-full");

        if (Src is { Length: > 0 } src)
        {
            return Div.Class(UiClass.Compose("avatar", Class))[
                Div.Class(frame)[Img.Src(src).Alt(Alt ?? Name ?? "")]
            ];
        }

        // daisyUI's documented placeholder shape: a <div> where the <img> would be, so the size and the
        // rounding are the same rules either way.
        return Div
            .Class(UiClass.Compose("avatar avatar-placeholder", Class))
            // The monogram is decoration — the NAME is what a reader needs, and "TP" read letter by letter is
            // not it. Said on the frame, with the initials themselves hidden.
            .Role(Name is null ? null : "img")
            .Aria(Name is null ? [] : new Dictionary<string, string?> { ["label"] = Name })[
            Div.Class(UiClass.Compose(frame, "bg-neutral text-neutral-content"))[
                Span.Class("text-xs font-medium").Aria("hidden", "true")[Initials(Name ?? "")]
            ]
        ];
    }

    /// <summary>The monogram for a name: the first letter of each of its first two words.</summary>
    /// <remarks>
    ///     Deliberately not "first and last". A name is not reliably two words in that order — "Pál Tamás" and
    ///     "Tamás Pál" are the same person written the way each culture writes it — so taking the ends would
    ///     give one person two different monograms depending on which form was stored.
    /// </remarks>
    internal static string Initials(string name)
    {
        var taken = new char[2];
        var count = 0;

        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (count == taken.Length)
            {
                break;
            }

            taken[count++] = char.ToUpperInvariant(word[0]);
        }

        // A name of nothing but spaces still has to draw something, and an empty circle looks broken.
        return count == 0 ? "?" : new string(taken, 0, count);
    }
}
