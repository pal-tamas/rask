#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

public class CrossParentKeyedMoveTests
{
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

    private sealed class Board : Component
    {
        private static readonly string[] Zones = { "todo", "doing", "done" };

        public readonly Dictionary<string, List<int>> Cols = new()
        {
            ["todo"] = new List<int> { 1, 2, 3 },
            ["doing"] = new List<int> { 4 },
            ["done"] = new List<int> { 5 }
        };

        private Child Column(string zone)
        {
            var cards = Cols[zone];
            var children = new List<Child>();
            foreach (var id in cards)
            {
                children.Add(Div(Key: id, Class: "card")[Div(Class: "card-body")[Span()[$"card{id}"]]]);
            }

            children.Add(Div(Key: $"{zone}-end", Class: "tail"));
            return Div(Key: zone, Class: "col")[
                Div(Class: "dd-column")[
                    Div(Class: "dd-column-header")[Span()[zone], Span()[cards.Count.ToString()]],
                    Div(Class: "dd-column-body")[children]
                ]
            ];
        }

        protected override RenderResult Render()
        {
            var cols = new List<Child>();
            foreach (var z in Zones)
            {
                cols.Add(Column(z));
            }

            return Div(Class: "board")[cols];
        }
    }
}
