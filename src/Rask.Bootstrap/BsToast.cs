namespace Rask.Bootstrap;

// A Bootstrap toast: <div class="toast [show]" role="alert">. Visibility is controlled by Show (the
// .show class); wire OnClose to dismiss. Auto-hide is intentionally not built in — that needs a timer
// the consumer owns; flip Show from your own logic.
public sealed class BsToast : BsBlock
{
    public bool? Show { get; set; }
    public string? Title { get; set; }
    public bool? HideClose { get; set; }

    public Callback? OnClose { get; set; }
    public CallbackAsync? OnCloseAsync { get; set; }

    private static readonly IReadOnlyDictionary<string, string?> ToastAria =
        new Dictionary<string, string?> { ["live"] = "assertive", ["atomic"] = "true" };

    protected override RenderResult Render()
    {
        var showHeader = Title is not null || HideClose is not true;
        return Div(Id: Id, Class: BsClass.Join("toast", Show is true ? "show" : null, Class),
            Role: "alert", Aria: ToastAria)[
                showHeader
                    ? Div(Class: "toast-header")[
                        Title is not null ? Strong(Class: "me-auto")[Title] : (Child)Fragment(),
                        HideClose is not true
                            ? BsCloseButton(OnClick: OnClose, OnClickAsync: OnCloseAsync)
                            : (Child)Fragment()]
                    : (Child)Fragment(),
                Div(Class: "toast-body")[Items]];
    }
}
