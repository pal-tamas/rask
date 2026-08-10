using Rask.Core;

namespace Rask.Core.Tests.Components;

// Guards the DOM-interface base-class layer: related tags must keep deriving from the shared base
// that mirrors their DOM interface (HTMLMediaElement, HTMLTableCellElement, …), and every base must
// remain an Element. If a tag is accidentally reparented back onto Element, the shared attributes
// would silently duplicate again — these assertions catch that.
public partial class HtmlInterfaceHierarchyTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(typeof(Audio), typeof(HtmlMediaElement))]
    [InlineData(typeof(Video), typeof(HtmlMediaElement))]
    [InlineData(typeof(Td), typeof(HtmlTableCellElement))]
    [InlineData(typeof(Th), typeof(HtmlTableCellElement))]
    [InlineData(typeof(Ins), typeof(HtmlModElement))]
    [InlineData(typeof(Del), typeof(HtmlModElement))]
    [InlineData(typeof(Col), typeof(HtmlTableColElement))]
    [InlineData(typeof(Colgroup), typeof(HtmlTableColElement))]
    [InlineData(typeof(Q), typeof(HtmlQuoteElement))]
    [InlineData(typeof(Blockquote), typeof(HtmlQuoteElement))]
    [InlineData(typeof(H1), typeof(HtmlHeadingElement))]
    [InlineData(typeof(H2), typeof(HtmlHeadingElement))]
    [InlineData(typeof(H3), typeof(HtmlHeadingElement))]
    [InlineData(typeof(H4), typeof(HtmlHeadingElement))]
    [InlineData(typeof(H5), typeof(HtmlHeadingElement))]
    [InlineData(typeof(H6), typeof(HtmlHeadingElement))]
    [InlineData(typeof(Thead), typeof(HtmlTableSectionElement))]
    [InlineData(typeof(Tbody), typeof(HtmlTableSectionElement))]
    [InlineData(typeof(Tfoot), typeof(HtmlTableSectionElement))]
    public void Tag_DerivesFrom_DomInterfaceBase(Type tag, Type domInterfaceBase)
    {
        Assert.True(domInterfaceBase.IsAbstract, $"{domInterfaceBase.Name} should be abstract");
        Assert.True(domInterfaceBase.IsAssignableFrom(tag),
            $"{tag.Name} should derive from {domInterfaceBase.Name}");
        Assert.True(typeof(Element).IsAssignableFrom(domInterfaceBase),
            $"{domInterfaceBase.Name} should derive from Element");
    }

    [Fact]
    public void Audio_IsMediaElement_WithNoBodyOfItsOwn() =>
        // Audio carries no attributes of its own — they live entirely on the shared base.
        Assert.IsAssignableFrom<HtmlMediaElement>(Audio);
}
