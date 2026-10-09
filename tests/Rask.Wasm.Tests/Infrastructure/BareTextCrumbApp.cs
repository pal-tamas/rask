using Rask.Core;

namespace Rask.Wasm.Tests.Infrastructure;

/// <summary>What the page is called: set by the page, shown by a crumb in the layout above it.</summary>
internal sealed class CrumbHeading
{
    public string Text { get; private set; } = string.Empty;

    public event EventHandler? Changed;

    public void Set(string text)
    {
        Text = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>#1238's leaf: it follows the heading and renders it as bare text, handed over as children.</summary>
internal sealed partial class BareTextCrumb(CrumbHeading heading) : Component
{
    protected override Task OnMount()
    {
        heading.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        heading.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Text[heading.Text];
}

/// <summary>A composite that bakes its children into an element of its own, as a breadcrumb item does.</summary>
internal sealed partial class BareTextCrumbItem : Component
{
    protected override Component? Render() => Div.Class("item")[Div.Class("step")[Children ?? []], Span[">"]];
}

/// <summary>A page that names itself as it mounts, and again when asked.</summary>
internal sealed partial class BareTextCrumbPage(CrumbHeading heading) : Component
{
    protected override Task OnMount()
    {
        heading.Set("Orders");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        Button.Id("rename").OnClick(() => heading.Set("Invoices"))["rename"],
        Button.Id("clear").OnClick(() => heading.Set(string.Empty))["clear"]
    ];
}

/// <summary>Mounts the page one render below the layout, so it mounts after the crumb above it was walked.</summary>
internal sealed partial class BareTextCrumbOutlet : Component
{
    protected override Component? Render() => Main[BareTextCrumbPage];
}

internal sealed partial class BareTextCrumbApp : Component
{
    protected override Component? HeadAssets => Title["bare-text-crumb"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => [Nav[BareTextCrumbItem[BareTextCrumb]], BareTextCrumbOutlet];
}
