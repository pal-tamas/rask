using System.Globalization;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>ResizeObserver</c>, from Rask.Web — report an element's size as it changes. The box below is observed;
///     toggle its width (or resize the window) and the browser hands the new size to the C# handler.
/// </summary>
public sealed partial class ResizeObserverDemo : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ElementRef _box = ElementRef.New();
    private Types.ResizeObserver? _observer;
    private double _width;
    private double _height;
    private bool _wide = true;

    protected override async Task OnFirstRender()
    {
        _observer ??= await ResizeObserver.Create(entries =>
        {
            var size = entries[^1].ContentRect;
            (_width, _height) = (size.Width, size.Height);
        });
        await _observer.Observe(_box);
    }

    protected override async Task OnUnmount()
    {
        if (_observer is not null)
        {
            await _observer.Disconnect();
            await _observer.DisposeAsync();
        }
    }

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("text-sm text-ui-muted mb-2")[
                    "Observed size: ",
                    Code.Id("resize-value")[
                        _width > 0 ? $"{_width.ToString("0", Inv)} × {_height.ToString("0", Inv)} px" : "(measuring…)"]
                ],
                Ui.Button.Class("mb-2")
                    .Id("resize-toggle")
                    .OnClick(() => _wide = !_wide)["Toggle width"],
                Div
                    .Ref(_box)
                    .Id("resize-box")
                    .Class("p-4 rounded bg-ui-well text-center", _wide ? "w-full" : "w-1/2")[
                    "📐 observed box (resize the window too)"
                ]
            ];
}
