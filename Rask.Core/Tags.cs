using Rask.Core.Forms;
using Rask.Core.Routing;
using F = Rask.Core.Components;

namespace Rask.Core;

// Every concrete `Component` subclass in the framework now exposes its props as public
// settable properties, so the source generator emits factories under
// `Rask.Core.Components.Components` (HTML tags + Doctype/Fragment/Text/Raw/ErrorBoundary/…),
// `Rask.Core.Routing.Components` (Router/Outlet), etc.
//
// This file carries the things the generator can't synthesise:
//   • `Router()` — zero-arg overload that pulls defaults from `RouteRegistry.BuildTree()`.
//   • `Route<T>(template?)` — record-shaped, not a Component.
//   • `Form<TModel>(...)` — generic-method overload that flattens typed valid/invalid
//     submit handlers into the non-generic Form factory.
//   • Positional `Fragment(child1, child2, …)` / `Doctype()` / `Text(...)` / `Raw(...)`
//     overloads. The generated factories include the inherited `Id`/`Class`/`Style`/`Data`
//     params before `Children`, which means positional children calls don't compile against
//     them — these wrappers preserve the established `Fragment(c1, c2)` DSL.
public static partial class Tags
{
    public static Route Route<T>(string template, IReadOnlyList<Route>? SubRoutes = null) where T : Component
        => new(typeof(T), template, SubRoutes);

    public static Route Route<T>(IReadOnlyList<Route>? SubRoutes = null) where T : Component
        => new(typeof(T), RouteTemplateResolver.GetLocalTemplate(typeof(T)), SubRoutes);

    public static Router Router() => Rask.Core.Routing.Components.Router(RouteRegistry.BuildTree());

    public static F.Fragment Fragment(params IEnumerable<Child> Children) => new(Children);

    public static F.Doctype Doctype() => new();

    public static F.Text Text(string value) => new(value);

    public static F.Raw Raw(string html) => new(html);

    // Generic Form overload: flattens model + valid/invalid handlers into the regular Form
    // ctor. Can't be auto-generated because the factory takes a TModel type parameter and
    // narrows the OnValidSubmit/OnInvalidSubmit delegate signatures.
    public static F.Form Form<TModel>(
        TModel Model,
        Action<TModel>? OnValidSubmit = null,
        Action<TModel>? OnInvalidSubmit = null,
        Func<TModel, Task>? OnValidSubmitAsync = null,
        Func<TModel, Task>? OnInvalidSubmitAsync = null,
        EditContext? Context = null,
        string? Enctype = null,
        string? Target = null,
        string? AcceptCharset = null,
        string? Autocomplete = null,
        bool Novalidate = false,
        string? Name = null,
        string? Id = null,
        string? Class = null,
        string? Style = null,
        IReadOnlyDictionary<string, string?>? Data = null,
        params IEnumerable<Child> Children) where TModel : class
    {
        var valid = (Delegate?)OnValidSubmit ?? OnValidSubmitAsync;
        var invalid = (Delegate?)OnInvalidSubmit ?? OnInvalidSubmitAsync;
        return F.Components.Form(
            Enctype: Enctype, Target: Target,
            AcceptCharset: AcceptCharset, Autocomplete: Autocomplete, Novalidate: Novalidate,
            Name: Name, Model: Model, OnValidSubmit: valid, OnInvalidSubmit: invalid,
            Context: Context, Id: Id, Class: Class, Style: Style, Data: Data, Children: Children);
    }
}
