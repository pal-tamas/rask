using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's phone field as a C# component: each example with its own code, and its reference.</summary>
[Route("phone")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiPhonePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Phone.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Phone)
            .Lead("An international phone number, with the country picked from a searchable list of flags, formatted as "
                  + "it is typed and normalised to E.164.")
            .Api(UiApi.Phone)[
            UiDocsExample.Key("ui-phone").Heading("Phone").File("PhoneExample.cs").Demo(PhoneExample).Testid("ui-phone"),
            UiDocsExample.Key("ui-phone-bound").Heading("Basic usage").File("PhoneBoundExample.cs").Demo(PhoneBoundExample).Testid("ui-phone-bound")
                .Blurb("Type 2015550123 and save: the field shows (201) 555-0123 and the member holds +12015550123. A number "
                       + "not yet complete is kept as typed."),
            UiDocsExample.Key("ui-phone-country").Heading("Default country").File("PhoneCountryExample.cs").Demo(PhoneCountryExample).Testid("ui-phone-country")
                .Blurb("The country of an empty number. A number typed with its calling code picks its own."),
            UiDocsExample.Key("ui-phone-countries").Heading("Country list").File("PhoneCountriesExample.cs").Demo(PhoneCountriesExample).Testid("ui-phone-countries")
                .Blurb("Only these in the list. It does not limit what can be typed: check that on the server."),
            UiDocsExample.Key("ui-phone-order").Heading("Country order").File("PhoneCountryOrderExample.cs").Demo(PhoneCountryOrderExample).Testid("ui-phone-order")
                .Blurb("These first; the rest follow alphabetically in the page's language."),
            UiDocsExample.Key("ui-phone-filled").Heading("Filled").File("PhoneFilledExample.cs").Demo(PhoneFilledExample).Testid("ui-phone-filled"),
            UiDocsExample.Key("ui-phone-sizes").Heading("Sizes").File("PhoneSizesExample.cs").Demo(PhoneSizesExample).Testid("ui-phone-sizes"),
            UiDocsExample.Key("ui-phone-disabled").Heading("Disabled and readonly").File("PhoneDisabledExample.cs").Demo(PhoneDisabledExample).Testid("ui-phone-disabled")
                .Blurb("A read-only field keeps its number and takes the focus; its list does not open."),
            UiDocsExample.Key("ui-phone-invalid").Heading("Invalid").File("PhoneInvalidExample.cs").Demo(PhoneInvalidExample).Testid("ui-phone-invalid"),
            UiPhoneBehaviour
        ];
}
