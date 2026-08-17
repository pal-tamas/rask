namespace Rask.Bootstrap;

// A confirm / cancel dialog on top of BsModal — the "are you sure?" prompt (delete confirmations and
// other destructive actions). Controlled like BsModal: hold a bool and wire Open + OnCancel to it. The
// body is the Message text, or custom children if any are passed via the indexer. The confirm button
// defaults to Danger (the destructive-confirm common case); wire OnConfirm to your action. Zero-JS,
// dismissible via Cancel / the × / a backdrop click (unless StaticBackdrop). Button labels default to
// neutral English so apps localise by passing ConfirmText / CancelText.

/// <summary>
///     A ready-made confirmation dialog for a destructive or irreversible action. Say what will happen in
///     <c>Message</c> and name the action in <c>ConfirmText</c> — "Delete 3 orders" beats "OK".
/// </summary>
public sealed partial class BsConfirmDialog : BsBlock
{
    /// <summary>Whether the dialog is shown.</summary>
    public bool? Open { get; set; }

    /// <summary>The dialog's heading.</summary>
    public new string? Title { get; set; }

    // Body text. Ignored when children are supplied (pass custom body content via the indexer instead).

    /// <summary>What is about to happen, and what it cannot undo.</summary>
    public string? Message { get; set; }

    /// <summary>The confirm button's label. Name the action rather than agreeing in the abstract.</summary>
    public string ConfirmText { get; set; } = "Confirm";

    /// <summary>The cancel button's label.</summary>
    public string CancelText { get; set; } = "Cancel";

    // Confirm button colour — the destructive-confirm common case.

    /// <summary>The confirm button's colour — <c>Danger</c> for anything destructive.</summary>
    public BsColor ConfirmColor { get; set; } = BsColor.Danger;

    // Disable outside-click / × dismissal, forcing an explicit Confirm or Cancel.

    /// <summary>Keeps the dialog open when the backdrop is clicked.</summary>
    public bool? StaticBackdrop { get; set; }

    /// <summary>Runs when the user confirms.</summary>
    public Action? OnConfirm { get; set; }

    /// <summary>Runs when the user confirms, asynchronously.</summary>
    public Func<Task>? OnConfirmAsync { get; set; }

    /// <summary>Runs when the user cancels.</summary>
    public Action? OnCancel { get; set; }

    /// <summary>Runs when the user cancels, asynchronously.</summary>
    public Func<Task>? OnCancelAsync { get; set; }

    protected override Component? Render() =>
        Open is not true
            ? null
            : BsModal
                .Id(Id)
                .Class(Class)
                .Open(true)
                .Title(Title)
                .Centered(true)
                .StaticBackdrop(StaticBackdrop)
                .OnClose(OnCancel)
                .OnCloseAsync(OnCancelAsync)
                .Footer(Footer())[Body()];

    private new Component Footer() =>
    [
        BsButton.Color(BsColor.Secondary).OnClick(OnCancel).OnClickAsync(OnCancelAsync)[CancelText],
        BsButton.Color(ConfirmColor).OnClick(OnConfirm).OnClickAsync(OnConfirmAsync)[ConfirmText]
    ];

    private new IEnumerable<Component?> Body() =>
        Message is not null ? [P.Class(Margin.Bottom(0))[Message]] : Items;
}
