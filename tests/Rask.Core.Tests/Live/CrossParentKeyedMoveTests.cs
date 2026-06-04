using C = Rask.Core.Components.Generated;
using Rask.Core;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

public class CrossParentKeyedMoveTests
{
    private sealed class Board : Component
    {
        public Dictionary<string, List<int>> Cols = new()
        {
            ["todo"] = new() { 1, 2, 3 },
            ["doing"] = new() { 4 },
            ["done"] = new() { 5 },
        };
        private static readonly string[] Zones = { "todo", "doing", "done" };

        private Child Column(string zone)
        {
            var cards = Cols[zone];
            var children = new List<Child>();
            foreach (var id in cards)
                children.Add(C.Div(Key: id, Class: "card")[C.Div(Class: "card-body")[C.Span()[$"card{id}"]]]);
            children.Add(C.Div(Key: $"{zone}-end", Class: "tail"));
            return C.Div(Key: zone, Class: "col")[
                C.Div(Class: "dd-column")[
                    C.Div(Class: "dd-column-header")[C.Span()[zone], C.Span()[cards.Count.ToString()]],
                    C.Div(Class: "dd-column-body")[children]
                ]
            ];
        }

        protected override RenderResult Render()
        {
            var cols = new List<Child>();
            foreach (var z in Zones) cols.Add(Column(z));
            return C.Div(Class: "board")[cols];
        }
    }

    [Fact]
    public void CrossParentKeyedMove_DoesNotCycle()
    {
        var board = new Board();
        var html1 = board.RenderAsLiveRoot();
        Assert.Contains("card2", html1);

        board.Cols["todo"].Remove(2);
        board.Cols["done"].Insert(0, 2);

        var html2 = board.RenderAsLiveRoot();
        var count = html2.Split("card2").Length - 1;
        Assert.True(count == 1, $"expected card2 once, got {count}");
    }
}
