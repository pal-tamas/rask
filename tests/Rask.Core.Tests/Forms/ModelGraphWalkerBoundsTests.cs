using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// The walk a validation makes of a model used to follow everything: an IQueryable (running it), a chain as deep as
// the data, and any number of objects — on every validation.
public sealed class ModelGraphWalkerBoundsTests
{
    [Fact]
    public void A_query_on_the_model_is_never_enumerated()
    {
        var query = new CountingQuery();
        var model = new WithQuery { Orders = query };

        var walked = ModelGraphWalker.Walk(model).ToList();

        Assert.Equal(0, query.Enumerations);
        Assert.Single(walked);
    }

    [Fact]
    public void A_chain_deeper_than_the_cap_is_walked_only_to_the_cap()
    {
        var root = new Link();
        var at = root;
        for (var i = 0; i < ModelGraphWalker.MaxDepth * 2; i++)
        {
            at = at.Next = new Link();
        }

        var walked = ModelGraphWalker.Walk(root).Count();

        Assert.Equal(ModelGraphWalker.MaxDepth + 1, walked);
    }

    [Fact]
    public void A_graph_wider_than_the_cap_stops_at_the_cap()
    {
        var model = new Wide { Items = Enumerable.Range(0, ModelGraphWalker.MaxNodes * 2).Select(static _ => new Link()).ToList() };

        var walked = ModelGraphWalker.Walk(model).Count();

        Assert.True(walked <= ModelGraphWalker.MaxNodes + 1, $"walked {walked}");
    }

    private sealed class WithQuery
    {
        public IQueryable<Link>? Orders { get; set; }
    }

    private sealed class Wide
    {
        public List<Link> Items { get; set; } = [];
    }

    private sealed class Link
    {
        public Link? Next { get; set; }
    }

    private sealed class CountingQuery : IQueryable<Link>
    {
        private readonly IQueryable<Link> _inner = new List<Link> { new() }.AsQueryable();

        public int Enumerations { get; private set; }

        public Type ElementType => _inner.ElementType;

        public System.Linq.Expressions.Expression Expression => _inner.Expression;

        public IQueryProvider Provider => _inner.Provider;

        public IEnumerator<Link> GetEnumerator()
        {
            Enumerations++;
            return _inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
