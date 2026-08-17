namespace Rask.Core.Routing;

/// <summary>
///     Makes a component a page at <paramref name="template" />. Registration is the attribute — the
///     generator finds it and builds the route table, so there is no list to keep in step.
/// </summary>
/// <remarks>
///     A segment in braces is a parameter, optionally with a constraint:
///     <c>[Route("/users/{id:int}")]</c> binds <c>id</c> to a matching <c>[RouteParam]</c> property and
///     only matches when the segment really is an integer.
///     <para>
///         Applying it more than once gives one page several URLs, which is how an old path is kept
///         working after a rename. Under a <c>ParentRoute</c> the template is relative to the parent, and
///         an empty one marks the layout's default child.
///     </para>
///     <para>
///         Link to a route through the generated <c>Routes</c> helpers rather than by writing the path as
///         a string — then a changed template is a compile error instead of a broken link.
///     </para>
/// </remarks>
/// <param name="template">The URL template, such as <c>/users/{id:int}</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RouteAttribute(string template) : Attribute
{
    /// <summary>The URL template this page is registered under.</summary>
    public string Template { get; } = template;
}
