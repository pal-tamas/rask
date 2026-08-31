using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="INavigatorInfo" /> — read-only navigator facts (online, language, user agent).</summary>
public sealed partial class NavigatorInfoDemo(INavigatorInfo navigator) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Button.Class($"{Ui.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("nav-read")
                    .OnClickAsync(Read)[
                    "Read navigator info"],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Info: ", Code.Id("nav-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("nav-status")[_status ?? "(idle)"]]
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
