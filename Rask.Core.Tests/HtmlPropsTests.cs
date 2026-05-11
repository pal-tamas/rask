namespace Rask.Core.Tests;

public class HtmlPropsTests
{
    [Fact]
    public void ToAttributes_AllNull_YieldsEmpty()
    {
        var props = new TestProps();
        Assert.Empty(props.ToAttributes());
    }

    [Fact]
    public void ToAttributes_AllSet_YieldsIdClassStyleInThatOrder()
    {
        var props = new TestProps("i", "c", "s");
        var pairs = props.ToAttributes().ToArray();

        Assert.Equal(3, pairs.Length);
        Assert.Equal(new KeyValuePair<string, string?>("id", "i"), pairs[0]);
        Assert.Equal(new KeyValuePair<string, string?>("class", "c"), pairs[1]);
        Assert.Equal(new KeyValuePair<string, string?>("style", "s"), pairs[2]);
    }

    [Fact]
    public void ToAttributes_DataDictionary_ExpandsKeysWithDataPrefix()
    {
        var data = new Dictionary<string, string?> { ["test-id"] = "primary" };
        var props = new TestProps(Data: data);

        var pair = Assert.Single(props.ToAttributes());
        Assert.Equal(new KeyValuePair<string, string?>("data-test-id", "primary"), pair);
    }

    [Fact]
    public void ToAttributes_DataValueNull_PropagatesNullValue()
    {
        var data = new Dictionary<string, string?> { ["flag"] = null };
        var props = new TestProps(Data: data);

        var pair = Assert.Single(props.ToAttributes());
        Assert.Equal("data-flag", pair.Key);
        Assert.Null(pair.Value);
    }

    [Fact]
    public void ToAttributes_MultipleDataKeys_PreservesDictionaryEnumerationOrder()
    {
        var data = new Dictionary<string, string?> { ["first"] = "1", ["second"] = "2", ["third"] = "3" };
        var props = new TestProps(Data: data);

        var keys = props.ToAttributes().Select(p => p.Key).ToArray();
        Assert.Equal(new[] { "data-first", "data-second", "data-third" }, keys);
    }

    private sealed record TestProps(
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null)
        : Component.Props(Id, Class, Style, Data);
}
