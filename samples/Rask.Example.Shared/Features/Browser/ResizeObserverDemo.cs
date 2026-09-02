using System.Globalization;
using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IResizeObserver" /> — report an element's size as it changes. The box below is observed;
///     toggle its width (or resize the window) and the browser pushes the new size to C#, which re-renders
///     the readout (the handler calls <c>StateHasChanged()</c>, the sanctioned pushed-update pattern).
/// </summary>
public sealed partial class ResizeObserverDemo(IResizeObserver observer) : Component, IAsyncDisposable
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ElementRef _box = ElementRef.New();
    private IAsyncDisposable? _observation;
    private double _width;
    private double _height;
    private bool _wide = true;

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _observation is not null)
        {
            return;
        }

        _observation = await observer.ObserveAsync(_box, size =>
        {
            _width = size.Width;
            _height = size.Height;
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-2")[
                    "Observed size: ",
                    Code.Id("resize-value")[
                        _width > 0 ? $"{_width.ToString("0", Inv)} × {_height.ToString("0", Inv)} px" : "(measuring…)"]
                ],
                Button
                    .Class($"{Tw.BtnOutlinePrimary} mb-2")
                    .Id("resize-toggle")
                    .OnClick(() => _wide = !_wide)["Toggle width"],
                Div
                    .Ref(_box)
                    .Id("resize-box")
                    .Class((_wide ? "w-full" : "w-1/2") + "p-4 rounded bg-slate-100 text-center")[
                    "📐 observed box (resize the window too)"
                ]
            ]
        ];

    public async ValueTask DisposeAsync()
    {
        if (_observation is not null)
        {
            await _observation.DisposeAsync();
        }
    }
}
