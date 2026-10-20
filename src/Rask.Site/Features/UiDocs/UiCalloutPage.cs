using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's callout as a C# component: its examples and its reference.</summary>
[Route("callout")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCalloutPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Callout.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Callout)
            .Lead("A message set apart from the page: an icon, a heading, text, and what to do about it. It "
                  + "announces nothing by itself; one that appears because something happened says so with Role.")
            .Files(["UiKitCalloutDemo.cs"])
            .Api(UiApi.Callout)
            .Demo(UiKitCalloutDemo)
            .Notes("Dismissing is the page's, as it is in Flux: the callout places the control, and the demo "
                   + "decides what pressing it does — here, two fields.");
}
