namespace Rask.Bootstrap;

// A confirm / cancel dialog on top of BsModal — the "are you sure?" prompt (delete confirmations and
// other destructive actions). Controlled like BsModal: hold a bool and wire Open + OnCancel to it. The
// body is the Message text, or custom children if any are passed via the indexer. The confirm button
// defaults to Danger (the destructive-confirm common case); wire OnConfirm to your action. Zero-JS,
// dismissible via Cancel / the × / a backdrop click (unless StaticBackdrop). Button labels default to
// neutral English so apps localise by passing ConfirmText / CancelText.
public sealed partial class BsConfirmDialog : BsBlock
{
    public bool? Open { get; set; }
    public new string? Title { get; set; }

    // Body text. Ignored when children are supplied (pass custom body content via the indexer instead).
    public string? Message { get; set; }

    public string ConfirmText { get; set; } = "Confirm";
    public string CancelText { get; set; } = "Cancel";

    // Confirm button colour — the destructive-confirm common case.
    public BsColor ConfirmColor { get; set; } = BsColor.Danger;

    // Disable outside-click / × dismissal, forcing an explicit Confirm or Cancel.
    public bool? StaticBackdrop { get; set; }

    public Action? OnConfirm { get; set; }
    public Func<Task>? OnConfirmAsync { get; set; }
    public Action? OnCancel { get; set; }
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

    private Component Footer() =>
    [
        BsButton.Color(BsColor.Secondary).OnClick(OnCancel).OnClickAsync(OnCancelAsync)[CancelText],
        BsButton.Color(ConfirmColor).OnClick(OnConfirm).OnClickAsync(OnConfirmAsync)[ConfirmText]
    ];

    private new IEnumerable<Component?> Body() =>
        Message is not null ? [P.Class(Margin.Bottom(0))[Message]] : Items;
}
