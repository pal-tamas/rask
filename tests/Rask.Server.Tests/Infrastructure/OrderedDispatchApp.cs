using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Test app for handler-dispatch ordering regression tests. Exposes ten
///     handlers (h0..h9) wired to <see cref="Buttons" />; each appends its
///     index to <see cref="Sequence" /> with a small yield in the middle so
///     the ThreadPool gets a realistic opportunity to interleave continuations.
///     With ordered dispatch the rendered HTML's `Sequence=N0N1N2…` content
///     always matches the order the buttons were clicked.
/// </summary>
public sealed class OrderedDispatchApp : Component
{
    private const int HandlerCount = 10;

    public string Sequence { get; private set; } = "";

    protected override RenderResult Render() =>
        [
            Doctype(),
            new Html()[
                new Head()[new Title()["ordered-dispatch"]],
                new Body()[
                    new P()[$"Sequence={Sequence}"],
                    Buttons()
                ]
            ]
        ];

    private Component Buttons()
    {
        var children = new List<Child>();
        for (var i = 0; i < HandlerCount; i++)
        {
            var captured = i;
            children.Add(Button(OnClickAsync: () => RecordAsync(captured), Key: captured)[$"#{captured}"]);
        }

        return Fragment()[children.ToArray()];
    }

    private async Task RecordAsync(int index)
    {
        // Yield inside the handler so the continuation runs on a fresh ThreadPool
        // tick. A non-FIFO dispatcher would let later-spawned handlers race
        // ahead during this yield and append their index first.
        await Task.Yield();
        Sequence += index.ToString();
    }
}
