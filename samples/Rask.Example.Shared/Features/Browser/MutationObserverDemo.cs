using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IMutationObserver" /> — observe DOM changes (children, attributes, text) on an element.
///     Mutate the watched box with the buttons: the browser pushes each <c>MutationRecord</c> to C#, which
///     updates the tally (the handler calls <c>StateHasChanged()</c>, the sanctioned pattern for an
///     externally-pushed update).
/// </summary>
public sealed partial class MutationObserverDemo(IMutationObserver observer) : Component, IAsyncDisposable
{
    private readonly ElementRef _target = ElementRef.New();
    private IAsyncDisposable? _observation;
    private int _items = 1;
    private bool _highlight;
    private int _childChanges;
    private int _attrChanges;
    private string _last = "(none yet)";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _observation is not null)
        {
            return;
        }

        _observation = await observer.ObserveAsync(_target, entry =>
        {
            if (entry.Type == "attributes")
            {
                _attrChanges++;
            }
            else
            {
                _childChanges++;
            }

            _last = entry.Type == "attributes"
                ? $"attributes ({entry.AttributeName})"
                : $"childList (+{entry.AddedCount} / -{entry.RemovedCount})";
            StateHasChanged();
            return Task.CompletedTask;
        }, new MutationOptions { ChildList = true, Attributes = true, Subtree = true });
    }

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                    Button.Class(Ui.BtnPrimary).Id("mo-add").OnClick(() => _items++)["Add item"],
                    Button
                        .Class(Ui.BtnOutlinePrimary)
                        .Id("mo-remove")
                        .OnClick(() => { if (_items > 0) _items--; })["Remove item"],
                    Button
                        .Class(Ui.BtnOutlineSecondary)
                        .Id("mo-toggle")
                        .OnClick(() => _highlight = !_highlight)["Toggle attribute"]
                ],
                Div
                    .Ref(_target)
                    .Id("mo-target")
                    .Class("border rounded p-3 mb-3" + (_highlight ? " border-warning bg-warning-subtle" : ""))[
                    Ul.Class("mb-0")[
                        Enumerable.Range(1, _items).Select(i => Li.Key(i.ToString())[$"item {i}"])
                    ]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "childList changes: ", Code.Id("mo-child")[$"{_childChanges}"],
                    " · attribute changes: ", Code.Id("mo-attr")[$"{_attrChanges}"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Last: ", Code.Id("mo-last")[_last]]
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
