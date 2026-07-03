namespace Rask.Bootstrap.Tests;

// Static-render assertions for BsConfirmDialog. It is controlled (Open drives visibility) and layers a
// confirm/cancel footer over BsModal; the button click handlers are covered live by consuming apps.
public class BsConfirmDialogTests
{
    [Fact]
    public void Closed_RendersNothing()
    {
        var html = BsConfirmDialog(Open: false, Message: "Delete this?").ToHtml();
        Assert.DoesNotContain("Delete this?", html);
        Assert.DoesNotContain("modal", html);
    }

    [Fact]
    public void Open_RendersMessageAndDefaultButtons_ConfirmIsDanger()
    {
        var html = BsConfirmDialog(Open: true, Title: "Delete", Message: "Delete this?").ToHtml();

        Assert.Contains("Delete this?", html);
        // Default neutral English labels.
        Assert.Contains("Confirm", html);
        Assert.Contains("Cancel", html);
        // The confirm button defaults to the destructive Danger colour.
        Assert.Contains("btn-danger", html);
        Assert.Contains("btn-secondary", html);
    }

    [Fact]
    public void CustomLabelsAndColor_AreApplied()
    {
        var html = BsConfirmDialog(
            Open: true, Message: "Publish now?",
            ConfirmText: "Publish", CancelText: "Keep editing", ConfirmColor: BsColor.Primary).ToHtml();

        Assert.Contains("Publish", html);
        Assert.Contains("Keep editing", html);
        Assert.Contains("btn-primary", html);
        Assert.DoesNotContain("btn-danger", html);
    }

    [Fact]
    public void Children_OverrideTheMessage()
    {
        var html = BsConfirmDialog(Open: true)[P()["Custom body content"]].ToHtml();
        Assert.Contains("Custom body content", html);
    }
}
