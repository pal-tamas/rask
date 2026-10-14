using Rask.Core.Live;

namespace Rask;

/// <summary>
///     Flux's <c>flux:flag</c>: a flag from its two-letter Unicode region code, 3:2 or cropped to a circle.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Flag.Country("US")</c>. The drawings are static files the app serves from <c>/flags</c>, one
///     request per flag shown and none in the assembly: set <c>&lt;RaskUiWriteFlags&gt;true&lt;/RaskUiWriteFlags&gt;</c>
///     in the app's project and the build copies them into <c>wwwroot/flags</c>. A code with no drawing
///     shows the empty frame.
///     </para>
///     <para>
///     A flag is decoration by default (<c>alt=""</c>): the text beside it usually names the country
///     already. Give it <see cref="Alt" /> when nothing else does. <see cref="Src" /> draws anything the
///     registry does not hold — a subdivision, an organization, a historical flag.
///     </para>
/// </remarks>
public sealed partial class UiFlag : Component
{
    private const string Frame =
        "relative block shrink-0 overflow-hidden bg-zinc-100 dark:bg-zinc-800"
        + " after:absolute after:inset-0 after:rounded-[inherit] after:inset-ring after:inset-ring-black/7"
        + " dark:after:inset-ring-white/10";

    private static readonly UiPartMarker Marker = new("ui-flag");

    /// <summary>The Unicode region code, in either case: <c>"US"</c>, <c>"de"</c>.</summary>
    public string? Country { get; set; }

    /// <summary>An image of your own, which wins over <see cref="Country" />.</summary>
    public string? Src { get; set; }

    /// <summary>What the flag says, for a reader who cannot see it. Empty when unset: decoration.</summary>
    public string? Alt { get; set; }

    /// <summary>How wide it is. <see cref="Ui.FlagSize.Sm" /> (24px) when unset.</summary>
    public Ui.FlagSize? Size { get; set; }

    /// <summary>Crops it to a circle.</summary>
    public bool? Circle { get; set; }

    /// <summary>Classes for the call site, added to the flag's own.</summary>
    public string? Class { get; set; }

    // A second marker the flag carries for the control it is drawn in: Flux's phone marks its flags.
    private string? _alsoMarked;

    /// <summary>Marks the flag for the control it is drawn in as well (<c>data-ui-phone-country-flag</c>). The kit's own chain step.</summary>
    /// <param name="marker">The marker's name, without <c>data-</c>.</param>
    internal UiFlag AlsoMarked(string marker)
    {
        _alsoMarked = marker;
        // What a generated step does when it writes a new value: the flag is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var source = Src ?? Drawing();
        var marks = Src is null && Country is { } country ? Marker.With(null, "country", country.ToUpperInvariant()) : Marker.With(null);
        if (_alsoMarked is { } also)
        {
            marks = new Dictionary<string, string?>(marks, StringComparer.Ordinal) { ["data-" + also] = null };
        }

        var frame = Span
            .Class(UiClass.Compose(Frame, Width(), Circle == true ? "aspect-square rounded-full" : "aspect-3/2 rounded-[2px]", Class))
            .Data(marks);

        return source is null
            ? frame
            : frame[Img.Src(source).Alt(Alt ?? "").Loading(Loading.Lazy).Decoding(Decoding.Async).Class("size-full object-cover")];
    }

    private string? Drawing() =>
        Country is { } country && UiFlagRegistry.File(country) is { } file
            ? $"{LiveOptions.PathBase}/flags/{file}.svg?v={UiFlagRegistry.Version}"
            : null;

    // In :where(), which weighs nothing, so a w-* from the call site wins whatever order the sheet lists them in.
    private string Width() => Size switch
    {
        Ui.FlagSize.Xs => "[:where(&)]:w-5",
        Ui.FlagSize.Md => "[:where(&)]:w-8",
        Ui.FlagSize.Lg => "[:where(&)]:w-10",
        Ui.FlagSize.Xl => "[:where(&)]:w-12",
        _ => "[:where(&)]:w-6",
    };
}
