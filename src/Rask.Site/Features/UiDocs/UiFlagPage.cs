using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's flag as a C# component: each example with its own code, and its reference.</summary>
[Route("flag")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFlagPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Flag.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Flag)
            .Lead("A country's flag from its two-letter Unicode region code, 3:2 or cropped to a circle, in five "
                  + "widths. Each is one small file the page fetches when it shows it.")
            .Api(UiApi.Flag)[
            UiDocsExample.Key("ui-flag-basic").Heading("Flag").File("FlagExample.cs").Demo(FlagExample).Testid("ui-flag-basic")
                .Blurb("24px wide when it says nothing else."),
            UiDocsExample.Key("ui-flag-sizes").Heading("Sizes").File("FlagSizesExample.cs").Demo(FlagSizesExample).Testid("ui-flag-sizes")
                .Blurb("Xl 48px, Lg 40px, Md 32px, Sm 24px and Xs 20px wide."),
            UiDocsExample.Key("ui-flag-circle").Heading("Circle").File("FlagCircleExample.cs").Demo(FlagCircleExample).Testid("ui-flag-circle"),
            UiDocsExample.Key("ui-flag-country").Heading("Country select").File("FlagCountrySelectExample.cs").Demo(FlagCountrySelectExample).Testid("ui-flag-country")
                .Blurb("A flag in each option, and in the button once one is picked."),
            UiDocsExample.Key("ui-flag-currency").Heading("Currency select").File("FlagCurrencySelectExample.cs").Demo(FlagCurrencySelectExample).Testid("ui-flag-currency"),
            UiDocsExample.Key("ui-flag-analytics").Heading("Analytics card").File("FlagAnalyticsExample.cs").Demo(FlagAnalyticsExample).Testid("ui-flag-analytics"),
            UiDocsExample.Key("ui-flag-custom").Heading("Custom flags").File("FlagCustomExample.cs").Demo(FlagCustomExample).Testid("ui-flag-custom")
                .Blurb("Src takes any image: a subdivision, an organization, a historical flag."),
            UiDocsExample.Key("ui-flag-accessibility").Heading("Accessibility").File("FlagAccessibilityExample.cs").Demo(FlagAccessibilityExample).Testid("ui-flag-accessibility")
                .Blurb("Beside the country's name a flag is decoration; on its own, Alt says which country it is."),
            UiFlagBehaviour
        ];
}
