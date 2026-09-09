namespace Rask.Ui;

/// <summary>
/// A stack of sections where opening one closes the others.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open" /> is the key of the section showing, and <c>null</c> is all of them closed — so the
/// page owns which one it is and can open a section in response to something that happened elsewhere.
/// That is the difference from a run of <see cref="UiCollapse" /> sharing a <c>Group</c>, which the
/// browser mutually excludes without telling anyone which one won.
/// </para>
/// <para>
/// Each section needs a <c>Key</c>, and it is the identity <see cref="Open" /> names as well as the one
/// reconciliation uses.
/// </para>
/// </remarks>
public sealed partial class UiAccordion : Component
{
    /// <summary>The key of the open section, or <c>null</c> for none.</summary>
    public string? Open { get; set; }

    /// <summary>Runs with the key the reader asked to open, or <c>null</c> if they closed the open one.</summary>
    public Callback<string?>? OnOpen { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("join join-vertical w-full", Class))[
            // Each section reads Open and OnOpen off the accordion through context rather than being
            // handed them: a section is written by the CALLER, inside the accordion's children, so
            // there is no call site at which to pass them down.
            Context.Provide(new UiAccordionState(Open, OnOpen))[Children ?? []]
        ];
}
