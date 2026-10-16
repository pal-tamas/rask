namespace Rask;

/// <summary>
/// Every daisyUI class the kit writes, spelled out.
/// </summary>
/// <remarks>
/// <para>
/// <b>These have to be complete literals, and that is the entire reason this file exists.</b> daisyUI 5
/// emits a component's CSS only where Tailwind can SEE its class name in the scanned source, so building
/// one by concatenation — <c>"btn-" + tone</c> — produces a name no scanner ever reads. The class is then
/// absent from the compiled sheet and the component renders with no styling whatsoever: not misaligned,
/// not the wrong colour, unstyled. Nothing reports it. The build is green, the markup carries exactly the
/// class the call site asked for, and only a browser shows the difference.
/// </para>
/// <para>
/// Gathering them here rather than scattering the switches through the components keeps that rule in one
/// reviewable place, and lets <c>UiClassNamesTests</c> check every literal against the sheet the build
/// actually produced.
/// </para>
/// <para>
/// A member a component has no form for returns the empty string, so composing a class list always yields
/// valid markup rather than a dangling suffix.
/// </para>
/// </remarks>
internal static class UiClassNames
{
    internal static string ButtonTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "btn-neutral",
        Ui.Tone.Primary => "btn-primary",
        Ui.Tone.Secondary => "btn-secondary",
        Ui.Tone.Accent => "btn-accent",
        Ui.Tone.Info => "btn-info",
        Ui.Tone.Success => "btn-success",
        Ui.Tone.Warning => "btn-warning",
        Ui.Tone.Error => "btn-error",
        _ => "",
    };

    internal static string ButtonSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "btn-xs",
        Ui.Size.Sm => "btn-sm",
        Ui.Size.Md => "btn-md",
        Ui.Size.Lg => "btn-lg",
        Ui.Size.Xl => "btn-xl",
        _ => "",
    };

    internal static string LoadingSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "loading-xs",
        Ui.Size.Sm => "loading-sm",
        Ui.Size.Md => "loading-md",
        Ui.Size.Lg => "loading-lg",
        Ui.Size.Xl => "loading-xl",
        _ => "",
    };

    internal static string StatusTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "status-neutral",
        Ui.Tone.Primary => "status-primary",
        Ui.Tone.Secondary => "status-secondary",
        Ui.Tone.Accent => "status-accent",
        Ui.Tone.Info => "status-info",
        Ui.Tone.Success => "status-success",
        Ui.Tone.Warning => "status-warning",
        Ui.Tone.Error => "status-error",
        _ => "",
    };

    internal static string StatusSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "status-xs",
        Ui.Size.Sm => "status-sm",
        Ui.Size.Md => "status-md",
        Ui.Size.Lg => "status-lg",
        Ui.Size.Xl => "status-xl",
        _ => "",
    };

    internal static string MenuSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "menu-xs",
        Ui.Size.Sm => "menu-sm",
        Ui.Size.Md => "menu-md",
        Ui.Size.Lg => "menu-lg",
        Ui.Size.Xl => "menu-xl",
        _ => "",
    };

    internal static string TableSize(Ui.Size value) => value switch
    {
        Ui.Size.Xs => "table-xs",
        Ui.Size.Sm => "table-sm",
        Ui.Size.Md => "table-md",
        Ui.Size.Lg => "table-lg",
        Ui.Size.Xl => "table-xl",
        _ => "",
    };

    /// <summary>The classes that hide a data-grid column below a breakpoint.</summary>
    /// <remarks>
    /// Table mode only. <c>sm:</c> comes first, so below <c>sm</c> — where the grid lists every cell as its own
    /// labelled line — nothing is hidden; <see cref="Ui.Breakpoint.Sm" /> is no change, because the table starts
    /// there. Variants only, never a bare <c>hidden</c>, for the cross-sheet reason Ui.DataGrid's cells record.
    /// </remarks>
    internal static string ColumnShowFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Md => "sm:max-md:hidden",
        Ui.Breakpoint.Lg => "sm:max-lg:hidden",
        Ui.Breakpoint.Xl => "sm:max-xl:hidden",
        _ => "",
    };

    /// <summary>The background a data-grid row takes for a tone.</summary>
    /// <remarks>A tint, not a fill: the row's text stays the body ink, so every tone reads at the same contrast.</remarks>
    internal static string RowTone(Ui.Tone value) => value switch
    {
        Ui.Tone.Neutral => "bg-neutral/10",
        Ui.Tone.Primary => "bg-primary/10",
        Ui.Tone.Secondary => "bg-secondary/10",
        Ui.Tone.Accent => "bg-accent/10",
        Ui.Tone.Info => "bg-info/10",
        Ui.Tone.Success => "bg-success/10",
        Ui.Tone.Warning => "bg-warning/10",
        Ui.Tone.Error => "bg-error/10",
        _ => "",
    };

    internal static string LoadingShape(Ui.LoadingShape value) => value switch
    {
        Ui.LoadingShape.Dots => "loading-dots",
        Ui.LoadingShape.Ring => "loading-ring",
        Ui.LoadingShape.Ball => "loading-ball",
        Ui.LoadingShape.Bars => "loading-bars",
        Ui.LoadingShape.Infinity => "loading-infinity",
        _ => "loading-spinner",
    };

    /// <summary>The ink a value takes when it reports a problem; null for a value that reports none.</summary>
    internal static string? ValueTone(Ui.Tone? value) => value switch
    {
        Ui.Tone.Error => "text-ui-danger-ink",
        Ui.Tone.Warning => "text-ui-warn-ink",
        _ => null,
    };

    /// <summary>The edge a drawer opens from.</summary>
    /// <remarks>
    ///     A drawer opens from the left or the right edge, and daisyUI's one class for it is <c>drawer-end</c>,
    ///     so only <see cref="Ui.Position.Right" /> writes anything.
    /// </remarks>
    internal static string DrawerPosition(Ui.Position value) => value switch
    {
        Ui.Position.Right => "drawer-end",
        _ => "",
    };
}
