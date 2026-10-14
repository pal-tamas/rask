namespace Rask;

/// <summary>
///     Flux's phone field, as measured on <c>fluxui.dev/components/phone</c>: an input group of the country
///     button — Flux's own listbox button — and the number's input, fused into one box.
/// </summary>
/// <remarks>
///     The button is drawn at the INPUT's sizes (its line is the input's 22px, not the select's 20px), the list
///     is the select's with a search field, and a row holds a flag, the country's name and its calling code.
/// </remarks>
internal static class UiPhoneLook
{
    /// <summary>Flux's <c>ui-phone</c>.</summary>
    internal const string Root = "block w-full min-w-0";

    /// <summary>Flux's input group, which holds the two halves.</summary>
    internal const string Group = "flex w-full";

    /// <summary>Flux's <c>ui-select</c> around the country button: as wide as what it shows.</summary>
    internal const string Country = "block shrink-0";

    // The button's shape: square where it meets the number.
    private const string Shape =
        "group/select-button cursor-default overflow-hidden flex items-center w-full rounded-e-none";

    // Red when the field is invalid: the root says so, and the button takes it from there, as Flux's does. No
    // shadow in dark, as the input has none there.
    private const string Invalid =
        "in-data-invalid:border-red-500 dark:in-data-invalid:border-red-500 in-data-invalid:shadow-none dark:shadow-none";

    // Filled, the button keeps one line: where it meets the number.
    private const string FilledButton =
        "border-0 border-e border-zinc-200 dark:border-white/10 shadow-none bg-zinc-800/5 dark:bg-white/10 "
        + "in-data-invalid:border in-data-invalid:border-red-500 dark:in-data-invalid:border-red-500";

    /// <summary>The country button at <paramref name="size" />, drawn <paramref name="variant" />.</summary>
    internal static string Button(Ui.PhoneSize size, Ui.PhoneVariant variant) => UiClass.Compose(
        Shape,
        ButtonSize(size),
        variant == Ui.PhoneVariant.Filled ? FilledButton : UiSelectLook.Button + " " + Invalid);

    // The input's heights, lines and corners; measured, the two smaller buttons keep 8px around what they show.
    private static string ButtonSize(Ui.PhoneSize size) => size switch
    {
        Ui.PhoneSize.Sm => "h-8 py-2 ps-3 pe-2 text-sm leading-[1.125rem] rounded-md",
        Ui.PhoneSize.Xs => "h-6 py-2 ps-3 pe-2 text-xs leading-[1.125rem] rounded-md",
        _ => "h-10 py-2 ps-3 pe-3 text-base sm:text-sm leading-[1.375rem] rounded-lg",
    };

    // The button's ink, as the select's: what it shows, paler when disabled. Filled, it is the filled input's in dark.
    private const string Ink =
        "text-zinc-700 group-disabled/select-button:text-zinc-500 dark:group-disabled/select-button:text-zinc-400";

    private static string Gap(Ui.PhoneSize size) => size == Ui.PhoneSize.Xs ? "gap-1.5" : "gap-2";

    /// <summary>What the button shows, Flux's <c>ui-selected</c>: closer together beside the smallest text.</summary>
    internal static string Selected(Ui.PhoneSize size, Ui.PhoneVariant variant) =>
        UiClass.Compose("flex flex-1 min-w-0 text-start", Ink, DarkInk(variant), Gap(size));

    /// <summary>Flux's <c>data-flux-phone-country-initial</c>: the server's first drawing of the country, which its script hides.</summary>
    internal static string Initial(Ui.PhoneSize size, Ui.PhoneVariant variant) =>
        UiClass.Compose("hidden items-center grow", Ink, DarkInk(variant), Gap(size));

    private static string DarkInk(Ui.PhoneVariant variant) => variant == Ui.PhoneVariant.Filled ? "dark:text-zinc-200" : "dark:text-zinc-300";

    /// <summary>A row's flag, name and code, side by side: closer together in the button beside the smallest text.</summary>
    internal static string CountryRow(Ui.PhoneSize size) => "flex items-center " + Gap(size);

    /// <summary>
    ///     The button's chevron: the select's, but a disabled one in dark is white at 40%, not 20%, on Flux's phone.
    /// </summary>
    internal const string Chevron =
        "ms-2 -me-1 text-zinc-400/75 group-hover/select-button:text-zinc-800 "
        + "dark:text-white/60 dark:group-hover/select-button:text-white "
        + "group-disabled/select-button:text-zinc-200! dark:group-disabled/select-button:text-white/40! "
        + "[[data-ui-select]:has(:popover-open)_&]:text-zinc-400/75! dark:[[data-ui-select]:has(:popover-open)_&]:text-white/60!";

    /// <summary>The number's input: the kit's input, square where it meets the button and with no line there.</summary>
    internal static string Input(Ui.PhoneSize size, Ui.PhoneVariant variant) => UiClass.Compose(
        UiInputLook.Control,
        UiInputLook.Size(InputSize(size)),
        UiInputLook.Padding(leading: false, trailing: false),
        UiInputLook.Variant(variant == Ui.PhoneVariant.Filled ? Ui.InputVariant.Filled : Ui.InputVariant.Outline),
        "rounded-s-none border-s-0");

    /// <summary>The same size, as the input's own enum names it.</summary>
    internal static Ui.InputSize InputSize(Ui.PhoneSize size) => size switch
    {
        Ui.PhoneSize.Sm => Ui.InputSize.Sm,
        Ui.PhoneSize.Xs => Ui.InputSize.Xs,
        _ => Ui.InputSize.Base,
    };

    /// <summary>The list: at least eighteen rem wide, a column whose list scrolls under the search field.</summary>
    internal const string Popup = "[:where(&)]:min-w-72 max-w-[calc(100vw-2rem)] " + UiListboxLook.Look + " " + UiListboxLook.Column;

    /// <summary>The number's half of the group: what is left of the row, and allowed to be narrower than its text.</summary>
    internal const string NumberHalf = "w-full relative block grow min-w-0";

    /// <summary>The country's name: on one line in the list, not shown in the button.</summary>
    internal const string Name = "truncate [[data-ui-select-button]_&]:hidden";

    /// <summary>The calling code: grey in the list, the button's own ink there.</summary>
    internal const string Code = "shrink-0 text-zinc-400 [[data-ui-select-button]_&]:text-inherit";

    /// <summary>The country's code, said to no one: Flux keeps it for its search.</summary>
    internal const string Iso = "sr-only";

    /// <summary>The flag at <paramref name="size" />: 20px, and 16px beside the smallest text.</summary>
    internal static string? Flag(Ui.PhoneSize size) => size == Ui.PhoneSize.Xs ? "w-4" : null;

    /// <summary>Flux's globe, in the button while no country is chosen.</summary>
    internal const string Globe = "size-5 text-zinc-400";
}
