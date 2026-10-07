using System.Text;

namespace Rask;

/// <summary>A kit part whose root element is a <see cref="HostedElement" /> it renders, rather than itself.</summary>
internal interface IUiHost
{
    /// <summary>The part's own attribute walk — id, class, data, ARIA, handlers — for the root it renders.</summary>
    void WriteHostAttributes(StringBuilder sb);
}
