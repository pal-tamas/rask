using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="INavigatorInfo" /> — read-only navigator facts (online, language, user agent).</summary>
public sealed class NavigatorInfoDemo(INavigatorInfo navigator) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Class: "mb-2", Id: "nav-read", OnClickAsync: Read)[
                    "Read navigator info"],
                Div(Class: "small text-secondary")["Info: ", Code(Id: "nav-value")[_value ?? "(not requested)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "nav-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            var online = await navigator.OnLineAsync();
            var language = await navigator.LanguageAsync();
            _value = $"online: {online}, language: {language}";
            _status = "Navigator read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
