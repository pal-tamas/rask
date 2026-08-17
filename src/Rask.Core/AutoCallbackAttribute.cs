namespace Rask.Core;

/// <summary>
///     Marks a callback property on an <see cref="Element" />-derived component as one the component
///     invokes ITSELF, so its setter wraps it and the component that owns the handler repaints.
/// </summary>
/// <remarks>
///     An element's delegate props normally go straight to the DOM, where handler-owner resolution
///     already repaints the parent — wrapping those would add a closure on the render hot path for
///     nothing, which is why they are assigned verbatim. A callback the component dispatches on its own
///     terms has no such mechanism behind it: <c>Form&lt;TModel&gt;</c>'s submit handlers run
///     from its submit bridge after validation, and without the wrap the component that supplied them
///     never re-renders.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AutoCallbackAttribute : Attribute;
