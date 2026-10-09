using Rask.Core;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>A value one component sets and another shows — a page title read by a crumb in the layout.</summary>
public sealed class CrumbTitle
{
    public string Value { get; private set; } = string.Empty;

    public event EventHandler? Changed;

    public void Set(string value)
    {
        Value = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The leaf as it was reported: its whole render is one bare text node, its words handed over as children.</summary>
public sealed partial class TextRootCrumb(CrumbTitle title) : Component
{
    protected override Task OnMount()
    {
        title.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        title.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Text[title.Value];
}

/// <summary>The same bare text, spelled with the value step.</summary>
public sealed partial class ValueTextCrumb(CrumbTitle title) : Component
{
    protected override Task OnMount()
    {
        title.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        title.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Text.Value(title.Value);
}

/// <summary>The same leaf with an element of its own, which is the shape that was reported to work.</summary>
public sealed partial class ElementRootCrumb(CrumbTitle title) : Component
{
    protected override Task OnMount()
    {
        title.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        title.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() => Span[title.Value];
}

/// <summary>A composite that bakes its children into an element of its own, as a breadcrumb item does.</summary>
public sealed partial class CrumbItem : Component
{
    protected override Component? Render() =>
        Div.Class("item")[Div.Class("step")[Children ?? []], Span.Class("separator")[">"]];
}

/// <summary>The routed page's stand-in: it names itself while it mounts, after the layout's crumb was built.</summary>
public sealed partial class CrumbPage(CrumbTitle title) : Component
{
    protected override Task OnMount()
    {
        title.Set("Orders");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P["page"],
        Button.Id("rename").OnClick(() => title.Set("Invoices"))["rename"],
        Button.Id("clear").OnClick(() => title.Set(string.Empty))["clear"]
    ];
}

/// <summary>Mounts the page one render level down, so it mounts after the crumb above it has been walked.</summary>
public sealed partial class CrumbOutlet : Component
{
    protected override Component? Render() => Main[CrumbPage];
}

public sealed partial class TextRootCrumbApp : Component
{
    protected override Component? HeadAssets => Title["text-root-crumb"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Nav.Id("text")[CrumbItem[TextRootCrumb]],
        Nav.Id("value")[CrumbItem[ValueTextCrumb]],
        Nav.Id("element")[CrumbItem[ElementRootCrumb]],
        CrumbOutlet
    ];
}

/// <summary>Lets a page reach the layout above it, as an app does through a scoped service.</summary>
public sealed class LayoutHandle
{
    public TitledLayout? Layout { get; set; }
}

/// <summary>A layout that is NOT the root: the root is forced dirty on every render, which would hide the question.</summary>
public sealed partial class TitledLayout(LayoutHandle handle) : Component
{
    private string _mounted = "none";
    private string _updated = "none";

    protected override Task OnMount()
    {
        handle.Layout = this;
        return Task.CompletedTask;
    }

    public void NameFromMount(string value)
    {
        _mounted = value;
        StateHasChanged();
    }

    public void NameFromUpdated(string value)
    {
        _updated = value;
        StateHasChanged();
    }

    protected override Component? Render() =>
        Div[H1.Id("mounted")[$"mounted={_mounted}"], H2.Id("updated")[$"updated={_updated}"], TitledOutlet];
}

public sealed partial class TitledOutlet : Component
{
    protected override Component? Render() => Main[TitledPage];
}

public sealed partial class TitledPage(LayoutHandle handle) : Component
{
    protected override Task OnMount()
    {
        handle.Layout!.NameFromMount("orders");
        return Task.CompletedTask;
    }

    protected override Task OnUpdated()
    {
        handle.Layout!.NameFromUpdated("orders");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P["page"],
        Button.Id("rename").OnClick(() => handle.Layout!.NameFromMount("invoices"))["rename"]
    ];
}

public sealed partial class TitledLayoutApp : Component
{
    protected override Component? HeadAssets => Title["titled-layout"];
    protected override string? HtmlLang => null;

    protected override Component? Render() => Section[TitledLayout];
}
